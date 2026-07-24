using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent;

public sealed class WorkspaceToolExecutor
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    private readonly BackgroundTaskManager taskManager;

    public WorkspaceToolExecutor(BackgroundTaskManager taskManager)
    {
        this.taskManager = taskManager;
    }

    public async Task<AgentToolResult> ExecuteToolAsync(
        string callId,
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Nessuna cartella di progetto autorizzata sul sistema.");
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = doc.RootElement;

            return toolName.ToLowerInvariant() switch
            {
                "list_dir" => ListDir(callId, toolName, root, workspaceRoot),
                "read_file" => await ReadFileAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "write_file" => await WriteFileAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "replace_file_content" => await ReplaceFileContentAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "grep_search" => GrepSearch(callId, toolName, root, workspaceRoot),
                "run_command" => RunCommand(callId, toolName, root, workspaceRoot),
                _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool non riconosciuto: {toolName}")
            };
        }
        catch (Exception ex)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Errore durante l'esecuzione del tool: {ex.Message}");
        }
    }

    private AgentToolResult ListDir(string callId, string toolName, JsonElement args, string rootPath)
    {
        string relative = args.TryGetProperty("relativePath", out var p) ? p.GetString() ?? "" : "";
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
        string relative = args.GetProperty("relativePath").GetString() ?? throw new ArgumentException("relativePath e obbligatorio");
        int? startLine = args.TryGetProperty("startLine", out var sl) && sl.ValueKind == JsonValueKind.Number ? sl.GetInt32() : null;
        int? endLine = args.TryGetProperty("endLine", out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

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
        string relative = args.GetProperty("relativePath").GetString() ?? throw new ArgumentException("relativePath e obbligatorio");
        string content = args.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

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
        string relative = args.GetProperty("relativePath").GetString() ?? throw new ArgumentException("relativePath e obbligatorio");
        string target = args.GetProperty("targetContent").GetString() ?? throw new ArgumentException("targetContent e obbligatorio");
        string replacement = args.GetProperty("replacementContent").GetString() ?? throw new ArgumentException("replacementContent e obbligatorio");

        string safePath = ResolveSafePath(rootPath, relative);
        if (!File.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}");
        }

        string original = await File.ReadAllTextAsync(safePath, cancellationToken);
        if (!original.Contains(target))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"TargetContent non trovato nel file {relative}. Verificare l'esattezza dei caratteri o dello spaziatura.");
        }

        string updated = original.Replace(target, replacement);
        await File.WriteAllTextAsync(safePath, updated, cancellationToken);

        return new AgentToolResult(callId, toolName, true, $"Sostituzione completata con successo nel file: {relative}");
    }

    private AgentToolResult GrepSearch(string callId, string toolName, JsonElement args, string rootPath)
    {
        string query = args.GetProperty("query").GetString() ?? throw new ArgumentException("query e obbligatorio");
        string relative = args.TryGetProperty("searchPath", out var sp) ? sp.GetString() ?? "" : "";

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
        string commandLine = args.GetProperty("commandLine").GetString() ?? throw new ArgumentException("commandLine e obbligatorio");
        bool isAsync = args.TryGetProperty("isAsync", out var a) && a.GetBoolean();

        if (isAsync)
        {
            var taskInfo = taskManager.StartTask(commandLine, rootPath);
            return new AgentToolResult(callId, toolName, true, $"Comando avviato in background con TaskID: {taskInfo.TaskId}\nUtilizzare manage_task per controllare lo stato ed i log.");
        }

        // Synchronous execution
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
