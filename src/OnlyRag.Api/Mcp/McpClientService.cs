using System.Diagnostics;
using System.Text.Json;

using OnlyRag.Core;
using OnlyRag.Core.Mcp;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Mcp;

public sealed class McpClientService : IMcpClientService, IDisposable
{
    private const string SettingsKey = "mcp.servers.config";
    private readonly ISettingsRepository _settingsRepository;
    private readonly AppStoragePaths _storagePaths;
    private readonly IMcpSseClient? _sseClient;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, Process> _runningProcesses = new(StringComparer.OrdinalIgnoreCase);

    public McpClientService(
        ISettingsRepository settingsRepository,
        AppStoragePaths storagePaths,
        IMcpSseClient? sseClient = null)
    {
        _settingsRepository = settingsRepository;
        _storagePaths = storagePaths;
        _sseClient = sseClient;
    }

    public async Task<IReadOnlyList<McpServerConfig>> GetConfiguredServersAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await LoadServersInternalAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<McpServerConfig> RegisterServerAsync(McpServerConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Id) || string.IsNullOrWhiteSpace(config.Name))
        {
            throw new ArgumentException("Server Id e Name non possono essere vuoti.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<McpServerConfig> list = (await LoadServersInternalAsync(cancellationToken)).ToList();
            int existingIndex = list.FindIndex(s => string.Equals(s.Id, config.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                list[existingIndex] = config;
            }
            else
            {
                list.Add(config);
            }

            await SaveServersInternalAsync(list, cancellationToken);
            return config;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UnregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            List<McpServerConfig> list = (await LoadServersInternalAsync(cancellationToken)).ToList();
            list.RemoveAll(s => string.Equals(s.Id, serverId, StringComparison.OrdinalIgnoreCase));
            StopProcessInternal(serverId);
            await SaveServersInternalAsync(list, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<McpServerConfig> servers = await GetConfiguredServersAsync(cancellationToken);
        List<McpToolDescriptor> tools = [];

        foreach (McpServerConfig server in servers.Where(s => s.IsEnabled))
        {
            try
            {
                // In MCP protocol via STDIO/JSON-RPC: tools/list request
                IReadOnlyList<McpToolDescriptor> serverTools = await FetchToolsFromServerAsync(server, cancellationToken);
                tools.AddRange(serverTools);
            }
            catch (Exception ex)
            {
                BackendLog.Write(_storagePaths, $"Errore recupero tool da server MCP '{server.Name}': {ex.Message}");
            }
        }

        return tools;
    }

    public async Task<McpToolCallResponse> CallToolAsync(McpToolCallRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<McpServerConfig> servers = await GetConfiguredServersAsync(cancellationToken);
        McpServerConfig? server = servers.FirstOrDefault(s => string.Equals(s.Id, request.ServerId, StringComparison.OrdinalIgnoreCase));
        if (server == null || !server.IsEnabled)
        {
            return new McpToolCallResponse(false, string.Empty, $"Server MCP con ID '{request.ServerId}' non trovato o disabilitato.");
        }

        // Fetch tool schema to validate arguments
        IReadOnlyList<McpToolDescriptor> tools = await FetchToolsFromServerAsync(server, cancellationToken);
        McpToolDescriptor? tool = tools.FirstOrDefault(t => string.Equals(t.Name, request.ToolName, StringComparison.OrdinalIgnoreCase));
        if (tool == null)
        {
            return new McpToolCallResponse(false, string.Empty, $"Tool '{request.ToolName}' non registrato nel server MCP '{server.Name}'.");
        }

        // Validate JSON Schema
        var (isValid, errorMessage) = McpSchemaValidator.Validate(tool.InputSchema, request.Arguments);
        if (!isValid)
        {
            return new McpToolCallResponse(false, string.Empty, $"Validazione JSON Schema fallita: {errorMessage}");
        }

        // Execute tool via MCP JSON-RPC
        try
        {
            string result = await InvokeToolOnServerAsync(server, request.ToolName, request.Arguments, cancellationToken);
            return new McpToolCallResponse(true, result);
        }
        catch (Exception ex)
        {
            return new McpToolCallResponse(false, string.Empty, $"Errore durante l'esecuzione del tool MCP: {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<McpToolDescriptor>> FetchToolsFromServerAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        if (server.Transport == McpTransportType.HttpSse)
        {
            if (_sseClient != null)
            {
                return await _sseClient.FetchToolsAsync(server, cancellationToken);
            }
            return [];
        }

        // STDIO Transport: JSON-RPC tools/list
        string requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { }
        });

        string? responseJson = await SendStdioJsonRpcAsync(server, requestJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return [];
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("result", out JsonElement resElement) &&
            resElement.TryGetProperty("tools", out JsonElement toolsArray) &&
            toolsArray.ValueKind == JsonValueKind.Array)
        {
            List<McpToolDescriptor> descriptors = [];
            foreach (JsonElement toolEl in toolsArray.EnumerateArray())
            {
                string name = toolEl.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                string desc = toolEl.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "";
                JsonElement schema = toolEl.TryGetProperty("inputSchema", out JsonElement s) ? s.Clone() : default;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    descriptors.Add(new McpToolDescriptor(server.Id, name, desc, schema));
                }
            }

            return descriptors;
        }

        return [];
    }

    private async Task<string> InvokeToolOnServerAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (server.Transport == McpTransportType.HttpSse)
        {
            if (_sseClient != null)
            {
                return await _sseClient.CallToolAsync(server, toolName, arguments, cancellationToken);
            }
            throw new InvalidOperationException("Transport HttpSse non supportato senza IMcpSseClient registrato.");
        }
        string requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        });

        string? responseJson = await SendStdioJsonRpcAsync(server, requestJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException("Risposta vuota da parte del server MCP.");
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("error", out JsonElement errElement))
        {
            string errMessage = errElement.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "Errore sconosciuto MCP" : "Errore MCP";
            throw new InvalidOperationException($"MCP Remote Error: {errMessage}");
        }

        if (doc.RootElement.TryGetProperty("result", out JsonElement resElement))
        {
            return resElement.GetRawText();
        }

        return responseJson;
    }

    private async Task<string?> SendStdioJsonRpcAsync(McpServerConfig server, string requestJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
        {
            throw new InvalidOperationException($"Comando STDIO mancante per il server MCP '{server.Name}'.");
        }

        Process process = GetOrStartProcess(server);
        await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);

        string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        return line;
    }

    private Process GetOrStartProcess(McpServerConfig server)
    {
        if (_runningProcesses.TryGetValue(server.Id, out Process? existingProcess) && !existingProcess.HasExited)
        {
            return existingProcess;
        }

        ProcessStartInfo psi = new()
        {
            FileName = server.Command!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (server.Arguments != null)
        {
            foreach (string arg in server.Arguments)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        if (server.EnvironmentVariables != null)
        {
            foreach (var kvp in server.EnvironmentVariables)
            {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        Process process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossibile avviare il processo per il server MCP '{server.Name}'.");
        _runningProcesses[server.Id] = process;
        return process;
    }

    private void StopProcessInternal(string serverId)
    {
        if (_runningProcesses.TryGetValue(serverId, out Process? process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore process kill errors on cleanup
            }
            finally
            {
                process.Dispose();
                _runningProcesses.Remove(serverId);
            }
        }
    }

    private async Task<List<McpServerConfig>> LoadServersInternalAsync(CancellationToken cancellationToken)
    {
        string? raw = await _settingsRepository.GetValueAsync(SettingsKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<McpServerConfig>>(raw) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveServersInternalAsync(List<McpServerConfig> servers, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(servers);
        await _settingsRepository.UpsertAsync(SettingsKey, json, cancellationToken);
    }

    public void Dispose()
    {
        _lock.Dispose();
        foreach (var kvp in _runningProcesses)
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(entireProcessTree: true);
                }
                kvp.Value.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        _runningProcesses.Clear();
    }
}
