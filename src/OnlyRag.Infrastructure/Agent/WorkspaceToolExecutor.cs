using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Infrastructure.Agent;

public sealed class WorkspaceToolExecutor
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    private readonly BackgroundTaskManager taskManager;
    private readonly ILoggingService? logger;

    public WorkspaceToolExecutor(BackgroundTaskManager taskManager, ILoggingService? logger = null)
    {
        this.taskManager = taskManager;
        this.logger = logger;
    }

    public async Task<AgentToolResult> ExecuteToolAsync(
        string callId,
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        logger?.LogTrace("AgentEngine", $"[TOOL EXEC START] Tool: '{toolName}', CallID: '{callId}', Args: {argumentsJson}");

        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            string err = "Nessuna cartella di progetto autorizzata sul sistema.";
            logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] {err}");
            return new AgentToolResult(callId, toolName, false, string.Empty, err);
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;

            AgentToolResult result = toolName.ToLowerInvariant() switch
            {
                "list_dir" => ListDir(callId, toolName, root, workspaceRoot),
                "read_file" or "view_file" => await ReadFileAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "write_file" or "write_to_file" => await WriteFileAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "replace_file_content" => await ReplaceFileContentAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "grep_search" => GrepSearch(callId, toolName, root, workspaceRoot),
                "run_command" => RunCommand(callId, toolName, root, workspaceRoot),
                "manage_task" => ManageTask(callId, toolName, root),
                "invoke_subagent" => await InvokeSubagentAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool non riconosciuto: {toolName}")
            };

            if (result.Success)
            {
                logger?.LogDebug("AgentEngine", $"[TOOL EXEC SUCCESS] Tool: '{toolName}', CallID: '{callId}'");
            }
            else
            {
                logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] Tool: '{toolName}', CallID: '{callId}', Error: {result.Error}");
            }

            return result;
        }
        catch (Exception ex)
        {
            string err = $"Errore durante l'esecuzione del tool '{toolName}': {ex.Message}";
            logger?.LogError("AgentEngine", err, ex);
            return new AgentToolResult(callId, toolName, false, string.Empty, err);
        }
    }

    private AgentToolResult ListDir(string callId, string toolName, JsonElement args, string rootPath)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target") ?? "";
        string safePath = ResolveSafePath(rootPath, relative);

        if (!Directory.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Cartella non trovata: {relative}");
        }

        var dir = new DirectoryInfo(safePath);
        var entries = dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)
            .Where(e => !e.Name.StartsWith('.') && e.Name != "node_modules" && e.Name != "bin" && e.Name != "obj")
            .Take(100)
            .Select(e => new
            {
                name = e.Name,
                isDirectory = (e.Attributes & FileAttributes.Directory) == FileAttributes.Directory,
                sizeBytes = (e.Attributes & FileAttributes.Directory) == FileAttributes.Directory ? 0 : ((FileInfo)e).Length,
                relativePath = Path.GetRelativePath(rootPath, e.FullName).Replace('\\', '/')
            });

        string json = JsonSerializer.Serialize(entries, s_indentedOptions);
        return new AgentToolResult(callId, toolName, true, json);
    }

    private async Task<AgentToolResult> ReadFileAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("Il parametro per il percorso del file ('relativePath' o 'path') è obbligatorio");

        int? startLine = GetArgInt(args, "startLine", "start", "fromLine");
        int? endLine = GetArgInt(args, "endLine", "end", "toLine");

        string safePath = ResolveSafePath(rootPath, relative);
        if (!File.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}");
        }

        string[] lines = await File.ReadAllLinesAsync(safePath, cancellationToken);
        if (startLine.HasValue || endLine.HasValue)
        {
            int start = Math.Max(1, startLine ?? 1) - 1;
            int end = Math.Min(lines.Length, endLine ?? lines.Length);

            if (start >= lines.Length)
            {
                return new AgentToolResult(callId, toolName, true, $"[File ha solo {lines.Length} righe]");
            }

            var sliced = lines.Skip(start).Take(end - start).Select((line, idx) => $"{start + idx + 1}: {line}");
            return new AgentToolResult(callId, toolName, true, string.Join("\n", sliced));
        }

        if (lines.Length > 800)
        {
            var truncated = lines.Take(800).Select((line, idx) => $"{idx + 1}: {line}");
            string output = string.Join("\n", truncated) + $"\n\n... [File troncato, mostra 800 righe su {lines.Length} totali]";
            return new AgentToolResult(callId, toolName, true, output);
        }

        var numberedLines = lines.Select((line, idx) => $"{idx + 1}: {line}");
        return new AgentToolResult(callId, toolName, true, string.Join("\n", numberedLines));
    }

    private async Task<AgentToolResult> WriteFileAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("Il parametro per il percorso del file ('relativePath' o 'path') è obbligatorio");

        string content = GetArgString(args, "content", "text", "code", "fileContent") ?? "";

        string safePath = ResolveSafePath(rootPath, relative);
        string? parent = Path.GetDirectoryName(safePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        await File.WriteAllTextAsync(safePath, content, cancellationToken);
        return new AgentToolResult(callId, toolName, true, $"File salvato con successo: {relative} ({content.Length} caratteri)");
    }

    private async Task<AgentToolResult> ReplaceFileContentAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("Il parametro per il percorso del file ('relativePath' o 'path') è obbligatorio");

        string target = GetArgString(args, "targetContent", "target", "oldContent", "old_string", "search")
            ?? throw new ArgumentException("Il parametro 'targetContent' è obbligatorio");

        string replacement = GetArgString(args, "replacementContent", "replacement", "newContent", "new_string", "replace")
            ?? throw new ArgumentException("Il parametro 'replacementContent' è obbligatorio");

        string safePath = ResolveSafePath(rootPath, relative);
        if (!File.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}");
        }

        string original = await File.ReadAllTextAsync(safePath, cancellationToken);
        if (!original.Contains(target))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"TargetContent non trovato nel file {relative}. Verificare l'esattezza dei caratteri o della spaziatura.");
        }

        string updated = original.Replace(target, replacement);
        await File.WriteAllTextAsync(safePath, updated, cancellationToken);

        return new AgentToolResult(callId, toolName, true, $"Sostituzione completata con successo nel file: {relative}");
    }

    private AgentToolResult GrepSearch(string callId, string toolName, JsonElement args, string rootPath)
    {
        string query = GetArgString(args, "query", "pattern", "search", "text")
            ?? throw new ArgumentException("Il parametro 'query' è obbligatorio");

        string relative = GetArgString(args, "searchPath", "path", "directory", "dir", "relativePath") ?? "";

        string targetDir = ResolveSafePath(rootPath, relative);
        if (!Directory.Exists(targetDir) && File.Exists(targetDir))
        {
            targetDir = Path.GetDirectoryName(targetDir)!;
        }

        var results = new List<string>();
        var files = Directory.EnumerateFiles(targetDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .Take(200);

        foreach (var file in files)
        {
            try
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                        results.Add($"{relFile}:{i + 1}: {lines[i].Trim()}");
                        if (results.Count >= 50) break;
                    }
                }
            }
            catch
            {
                // Ignora file non leggibili
            }

            if (results.Count >= 50) break;
        }

        if (results.Count == 0)
        {
            return new AgentToolResult(callId, toolName, true, $"Nessun risultato trovato per '{query}'.");
        }

        return new AgentToolResult(callId, toolName, true, string.Join("\n", results));
    }

    private AgentToolResult RunCommand(string callId, string toolName, JsonElement args, string rootPath)
    {
        string commandLine = GetArgString(args, "commandLine", "command", "cmd", "script")
            ?? throw new ArgumentException("Il parametro 'commandLine' è obbligatorio");

        bool isAsync = (args.TryGetProperty("isAsync", out var a) && a.GetBoolean()) ||
                       (args.TryGetProperty("async", out var a2) && a2.GetBoolean());

        if (isAsync)
        {
            var taskInfo = taskManager.StartTask(commandLine, rootPath);
            return new AgentToolResult(callId, toolName, true, $"Comando avviato in background con TaskID: {taskInfo.TaskId}\nUtilizzare manage_task per controllare lo stato ed i log.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{commandLine.Replace("\"", "\\\"")}\"",
            WorkingDirectory = rootPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Impossibile avviare il processo di shell.");
        }

        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string combined = string.IsNullOrWhiteSpace(stderr)
            ? stdout
            : $"{stdout}\n[STDERR]\n{stderr}";

        return new AgentToolResult(
            callId,
            toolName,
            process.ExitCode == 0,
            combined,
            process.ExitCode == 0 ? null : $"Processo terminato con exit code {process.ExitCode}");
    }

    private AgentToolResult ManageTask(string callId, string toolName, JsonElement args)
    {
        string action = GetArgString(args, "action", "act", "type") ?? "list";
        string taskId = GetArgString(args, "taskId", "id", "task") ?? "";
        string input = GetArgString(args, "input", "text") ?? "";

        if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
        {
            var tasks = taskManager.ListTasks();
            string json = JsonSerializer.Serialize(tasks, s_indentedOptions);
            return new AgentToolResult(callId, toolName, true, json);
        }
        else if (string.Equals(action, "kill", StringComparison.OrdinalIgnoreCase))
        {
            bool killed = taskManager.KillTask(taskId);
            return new AgentToolResult(callId, toolName, killed, killed ? $"Task {taskId} terminato." : $"Impossibile terminare il task {taskId}.");
        }
        else if (string.Equals(action, "send_input", StringComparison.OrdinalIgnoreCase))
        {
            bool sent = taskManager.SendInput(taskId, input);
            return new AgentToolResult(callId, toolName, sent, sent ? $"Input inviato al task {taskId}." : $"Impossibile inviare input al task {taskId}.");
        }
        else if (string.Equals(action, "status", StringComparison.OrdinalIgnoreCase))
        {
            var status = taskManager.GetTaskStatusAndLogs(taskId);
            if (status.HasValue)
            {
                string res = $"[STATUS: {status.Value.Info.IsRunning}]\nLOGS:\n{status.Value.Logs}";
                return new AgentToolResult(callId, toolName, true, res);
            }
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Task {taskId} non trovato.");
        }

        return new AgentToolResult(callId, toolName, false, string.Empty, $"Azione task sconosciuta: {action}");
    }

    private Task<AgentToolResult> InvokeSubagentAsync(
        string callId,
        string toolName,
        JsonElement args,
        string rootPath,
        CancellationToken cancellationToken)
    {
        string prompt = GetArgString(args, "prompt", "goal", "task") ?? "";

        return Task.FromResult(new AgentToolResult(
            callId,
            toolName,
            false,
            string.Empty,
            $"Il tool 'invoke_subagent' non è abilitato in questo ambiente. Esegui il lavoro direttamente utilizzando " +
            $"i tool list_dir, read_file, write_file, replace_file_content, grep_search e run_command. " +
            $"Obiettivo da eseguire direttamente: '{prompt}'"));
    }

    private static string? GetArgString(JsonElement root, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (root.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.String)
            {
                return elem.GetString();
            }
        }
        // Tentativo insensibile al maiuscolo/minuscolo
        foreach (var prop in root.EnumerateObject())
        {
            if (propertyNames.Any(p => p.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    return prop.Value.GetString();
                }
            }
        }
        return null;
    }

    private static int? GetArgInt(JsonElement root, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (root.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.Number)
            {
                return elem.GetInt32();
            }
        }
        return null;
    }

    private static string ResolveSafePath(string rootPath, string relativePath)
    {
        string root = Path.GetFullPath(rootPath);
        string target = Path.GetFullPath(Path.Combine(root, (relativePath ?? "").TrimStart('/', '\\')));

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Path Traversal bloccato: tentativo di accedere ad una risorsa esterna al workspace.");
        }

        return target;
    }
}
