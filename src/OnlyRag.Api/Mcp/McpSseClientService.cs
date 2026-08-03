using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Core.Mcp;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api.Mcp;

public sealed class McpSseClientService : IMcpSseClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppStoragePaths _storagePaths;
    private readonly ConcurrentDictionary<string, SseSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public McpSseClientService(HttpClient httpClient, AppStoragePaths storagePaths)
    {
        _httpClient = httpClient;
        _storagePaths = storagePaths;
    }

    public async Task<McpSseSessionStatus> ConnectAsync(McpServerConfig server, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (string.IsNullOrWhiteSpace(server.ServerUrl))
        {
            throw new ArgumentException($"ServerUrl non valido per il server MCP SSE '{server.Name}'.");
        }

        SseSession session = _sessions.GetOrAdd(server.Id, id => new SseSession(id, server.ServerUrl));

        await session.Lock.WaitAsync(cancellationToken);
        try
        {
            if (session.State == McpSseConnectionState.Connected && !string.IsNullOrEmpty(session.PostEndpoint))
            {
                return session.ToStatus();
            }

            session.State = McpSseConnectionState.Connecting;
            session.LastError = null;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, server.ServerUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                string? sessionId = null;
                if (response.Headers.TryGetValues("Mcp-Session-Id", out var values))
                {
                    sessionId = values.FirstOrDefault();
                }

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                // Read initial SSE frames to discover endpoint
                string? endpointUrl = null;
                int lineCount = 0;
                while (lineCount < 20)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken);
                    if (line == null) break;
                    lineCount++;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (line.StartsWith("event: endpoint", StringComparison.OrdinalIgnoreCase))
                    {
                        string? dataLine = await reader.ReadLineAsync(cancellationToken);
                        if (dataLine != null && dataLine.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                        {
                            endpointUrl = dataLine["data: ".Length..].Trim();
                            break;
                        }
                    }
                    else if (line.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
                    {
                        string data = line["data: ".Length..].Trim();
                        if (data.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            data.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                            data.StartsWith('/'))
                        {
                            endpointUrl = data;
                            break;
                        }
                    }
                }

                // If endpointUrl is relative, combine with base ServerUrl
                if (!string.IsNullOrEmpty(endpointUrl))
                {
                    if (Uri.TryCreate(new Uri(server.ServerUrl), endpointUrl, out var combined))
                    {
                        session.PostEndpoint = combined.ToString();
                    }
                    else
                    {
                        session.PostEndpoint = endpointUrl;
                    }
                }
                else
                {
                    // Fallback to ServerUrl if endpoint event omitted
                    session.PostEndpoint = server.ServerUrl;
                }

                session.SessionId = sessionId ?? Guid.NewGuid().ToString("N");
                session.State = McpSseConnectionState.Connected;
                session.LastConnectedAt = DateTimeOffset.UtcNow;
                session.ReconnectAttempts = 0;

                BackendLog.Write(_storagePaths, $"MCP SSE Connesso a '{server.Name}' (Endpoint: {session.PostEndpoint})");
                return session.ToStatus();
            }
            catch (Exception ex)
            {
                session.State = McpSseConnectionState.Failed;
                session.LastError = ex.Message;
                BackendLog.Write(_storagePaths, $"Errore connessione MCP SSE '{server.Name}': {ex.Message}");
                throw;
            }
        }
        finally
        {
            session.Lock.Release();
        }
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> FetchToolsAsync(McpServerConfig server, CancellationToken cancellationToken = default)
    {
        McpSseSessionStatus status = await EnsureConnectedWithAutoReconnectAsync(server, cancellationToken);
        string endpoint = status.PostEndpoint ?? server.ServerUrl!;

        string requestJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/list",
            @params = new { }
        });

        string responseJson = await SendPostJsonAsync(endpoint, status.SessionId, requestJson, cancellationToken);
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

    public async Task<string> CallToolAsync(McpServerConfig server, string toolName, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        McpSseSessionStatus status = await EnsureConnectedWithAutoReconnectAsync(server, cancellationToken);
        string endpoint = status.PostEndpoint ?? server.ServerUrl!;

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

        string responseJson = await SendPostJsonAsync(endpoint, status.SessionId, requestJson, cancellationToken);
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException($"Risposta vuota da parte del server MCP SSE '{server.Name}'.");
        }

        using JsonDocument doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("error", out JsonElement errElement))
        {
            string errMessage = errElement.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "Errore sconosciuto MCP SSE" : "Errore MCP SSE";
            throw new InvalidOperationException($"MCP SSE Remote Error: {errMessage}");
        }

        if (doc.RootElement.TryGetProperty("result", out JsonElement resElement))
        {
            return resElement.GetRawText();
        }

        return responseJson;
    }

    public async Task DisconnectAsync(string serverId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(serverId, out SseSession? session))
        {
            await session.Lock.WaitAsync(cancellationToken);
            try
            {
                session.State = McpSseConnectionState.Disconnected;
                session.SessionId = null;
                session.PostEndpoint = null;
            }
            finally
            {
                session.Lock.Release();
                session.Lock.Dispose();
            }
        }
    }

    public McpSseSessionStatus GetSessionStatus(string serverId)
    {
        return _sessions.TryGetValue(serverId, out var session)
            ? session.ToStatus()
            : new McpSseSessionStatus(serverId, McpSseConnectionState.Disconnected, null, null, null, null);
    }

    private async Task<McpSseSessionStatus> EnsureConnectedWithAutoReconnectAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        SseSession session = _sessions.GetOrAdd(server.Id, id => new SseSession(id, server.ServerUrl ?? string.Empty));
        if (session.State == McpSseConnectionState.Connected && !string.IsNullOrEmpty(session.PostEndpoint))
        {
            return session.ToStatus();
        }

        // Exponential backoff reconnect
        int maxAttempts = 3;
        int delayMs = 1000;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                session.State = McpSseConnectionState.Reconnecting;
                session.ReconnectAttempts = attempt;
                return await ConnectAsync(server, cancellationToken);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                if (attempt < maxAttempts)
                {
                    await Task.Delay(delayMs, cancellationToken);
                    delayMs *= 2;
                }
            }
        }

        session.State = McpSseConnectionState.Failed;
        session.LastError = lastEx?.Message ?? "Auto-reconnect fallito.";
        throw new InvalidOperationException($"Impossibile connettersi al server MCP SSE '{server.Name}': {session.LastError}", lastEx);
    }

    private async Task<string> SendPostJsonAsync(string endpoint, string? sessionId, string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };

        if (!string.IsNullOrEmpty(sessionId))
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Lock.Dispose();
        }
        _sessions.Clear();
    }

    private sealed class SseSession
    {
        public string ServerId { get; }
        public string ServerUrl { get; }
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public McpSseConnectionState State { get; set; } = McpSseConnectionState.Disconnected;
        public string? SessionId { get; set; }
        public string? PostEndpoint { get; set; }
        public string? LastError { get; set; }
        public DateTimeOffset? LastConnectedAt { get; set; }
        public int ReconnectAttempts { get; set; }

        public SseSession(string serverId, string serverUrl)
        {
            ServerId = serverId;
            ServerUrl = serverUrl;
        }

        public McpSseSessionStatus ToStatus() =>
            new(ServerId, State, SessionId, PostEndpoint, LastError, LastConnectedAt);
    }
}
