using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Agent;

public sealed class WorkspaceToolExecutor
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };

    private readonly BackgroundTaskManager taskManager;
    private readonly IHybridRetrievalService? retrievalService;
    private readonly IDocumentIngestionService? ingestionService;
    private readonly ImageGenerationService? imageGenerationService;
    private readonly ISubagentRunner? subagentRunner;
    private readonly ILoggingService? logger;

    public WorkspaceToolExecutor(
        BackgroundTaskManager taskManager,
        IHybridRetrievalService? retrievalService = null,
        IDocumentIngestionService? ingestionService = null,
        ImageGenerationService? imageGenerationService = null,
        ISubagentRunner? subagentRunner = null,
        ILoggingService? logger = null)
    {
        this.taskManager = taskManager;
        this.retrievalService = retrievalService;
        this.ingestionService = ingestionService;
        this.imageGenerationService = imageGenerationService;
        this.subagentRunner = subagentRunner;
        this.logger = logger;
    }

    public async Task<AgentToolResult> ExecuteToolAsync(
        string callId,
        string toolName,
        string argumentsJson,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
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
                "multi_replace_file_content" => await MultiReplaceFileContentAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "grep_search" => GrepSearch(callId, toolName, root, workspaceRoot),
                "git_diff_inspect" => GitDiffInspect(callId, toolName, root, workspaceRoot),
                "run_command" => RunCommand(callId, toolName, root, workspaceRoot),
                "manage_task" => ManageTask(callId, toolName, root),
                "web_search" or "search_web" => await WebSearchAsync(callId, toolName, root, cancellationToken),
                "ingest_office_doc" => await IngestOfficeDocAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "generate_image_onnx" => await GenerateImageOnnxAsync(callId, toolName, root, cancellationToken),
                "query_retrieval_index" or "rag_hybrid_search" or "rag_search" or "search_docs" => await QueryRetrievalIndexAsync(callId, toolName, root, cancellationToken),
                "plan_task" or "create_plan" or "update_plan" => PlanTask(callId, toolName, root),
                "reflect_step" or "reflect" or "self_reflection" => ReflectStep(callId, toolName, root),
                "ast_structural_refactor" or "refactor_symbol" => await AstStructuralRefactorAsync(callId, toolName, root, workspaceRoot, cancellationToken),
                "invoke_subagent" => await InvokeSubagentAsync(callId, toolName, root, workspaceRoot, onStep, cancellationToken),
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

        string safePath = ResolveSafePathWithSmartFallback(rootPath, relative, out string actualRelative);
        if (!File.Exists(safePath))
        {
            string suggestions = GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}.{suggestions}");
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

    public async Task<IReadOnlyList<AgentToolResult>> ExecuteToolsBatchAsync(
        IReadOnlyList<(string CallId, string ToolName, string ArgumentsJson)> calls,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var readOnlyTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "list_dir", "read_file", "view_file", "grep_search", "git_diff_inspect", "query_retrieval_index", "web_search"
        };

        bool allReadOnly = calls.All(c => readOnlyTools.Contains(c.ToolName));

        if (allReadOnly && calls.Count > 1)
        {
            var tasks = calls.Select(c => ExecuteToolAsync(c.CallId, c.ToolName, c.ArgumentsJson, workspaceRoot, cancellationToken: cancellationToken));
            var batchResults = await Task.WhenAll(tasks);
            return batchResults.ToList();
        }

        var seqResults = new List<AgentToolResult>();
        foreach (var c in calls)
        {
            var res = await ExecuteToolAsync(c.CallId, c.ToolName, c.ArgumentsJson, workspaceRoot, cancellationToken: cancellationToken);
            seqResults.Add(res);
        }
        return seqResults;
    }

    public static string GenerateUnifiedDiffPatch(string relativePath, string oldContent, string newContent)
    {
        string relPath = (relativePath ?? "").Replace('\\', '/');
        string[] oldLines = (oldContent ?? "").Replace("\r\n", "\n").Split('\n');
        string[] newLines = (newContent ?? "").Replace("\r\n", "\n").Split('\n');

        var sb = new StringBuilder();
        sb.AppendLine($"--- a/{relPath}");
        sb.AppendLine($"+++ b/{relPath}");

        int max = Math.Max(oldLines.Length, newLines.Length);
        int diffCount = 0;

        for (int i = 0; i < max; i++)
        {
            string? oldL = i < oldLines.Length ? oldLines[i] : null;
            string? newL = i < newLines.Length ? newLines[i] : null;

            if (oldL != newL)
            {
                if (oldL != null)
                {
                    sb.AppendLine($"- {oldL}");
                    diffCount++;
                }
                if (newL != null)
                {
                    sb.AppendLine($"+ {newL}");
                    diffCount++;
                }
            }
        }

        return diffCount > 0 ? sb.ToString() : string.Empty;
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

        string original = File.Exists(safePath) ? await File.ReadAllTextAsync(safePath, cancellationToken) : string.Empty;
        await File.WriteAllTextAsync(safePath, content, cancellationToken);
        string patch = GenerateUnifiedDiffPatch(relative, original, content);
        return new AgentToolResult(callId, toolName, true, $"File salvato con successo: {relative} ({content.Length} caratteri)", DiffPatch: patch);
    }

    private async Task<AgentToolResult> ReplaceFileContentAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("Il parametro per il percorso del file ('relativePath' o 'path') è obbligatorio");

        string target = GetArgString(args, "targetContent", "target", "oldContent", "old_string", "search")
            ?? throw new ArgumentException("Il parametro 'targetContent' è obbligatorio");

        string replacement = GetArgString(args, "replacementContent", "replacement", "newContent", "new_string", "replace")
            ?? throw new ArgumentException("Il parametro 'replacementContent' è obbligatorio");

        string safePath = ResolveSafePathWithSmartFallback(rootPath, relative, out string actualRelative);
        if (!File.Exists(safePath))
        {
            string suggestions = GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}.{suggestions}");
        }

        string original = await File.ReadAllTextAsync(safePath, cancellationToken);
        if (original.Contains(target))
        {
            string updated = original.Replace(target, replacement);
            await File.WriteAllTextAsync(safePath, updated, cancellationToken);
            string patch = GenerateUnifiedDiffPatch(relative, original, updated);
            return new AgentToolResult(callId, toolName, true, $"Sostituzione completata con successo nel file: {relative}", DiffPatch: patch);
        }

        // Fallback con normalizzazione fine riga (CRLF vs LF)
        string normOriginal = original.Replace("\r\n", "\n");
        string normTarget = target.Replace("\r\n", "\n");
        string normReplacement = replacement.Replace("\r\n", "\n");

        if (normOriginal.Contains(normTarget))
        {
            string updated = normOriginal.Replace(normTarget, normReplacement);
            if (original.Contains("\r\n"))
            {
                updated = updated.Replace("\n", "\r\n");
            }
            await File.WriteAllTextAsync(safePath, updated, cancellationToken);
            string patch = GenerateUnifiedDiffPatch(relative, original, updated);
            return new AgentToolResult(callId, toolName, true, $"Sostituzione completata (con normalizzazione a capo) nel file: {relative}", DiffPatch: patch);
        }

        // Fallback avanzato: confronto righe tollerante agli spazi di rientro (fuzzy line match)
        string[] origLines = normOriginal.Split('\n');
        string[] targetLines = normTarget.Split('\n');

        if (targetLines.Length > 0 && targetLines.Length <= origLines.Length)
        {
            for (int i = 0; i <= origLines.Length - targetLines.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < targetLines.Length; j++)
                {
                    if (!origLines[i + j].Trim().Equals(targetLines[j].Trim(), StringComparison.Ordinal))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    var newLinesList = new List<string>(origLines.Take(i));
                    newLinesList.Add(normReplacement);
                    newLinesList.AddRange(origLines.Skip(i + targetLines.Length));
                    string updated = string.Join("\n", newLinesList);
                    if (original.Contains("\r\n"))
                    {
                        updated = updated.Replace("\n", "\r\n");
                    }
                    await File.WriteAllTextAsync(safePath, updated, cancellationToken);
                    string patch = GenerateUnifiedDiffPatch(relative, original, updated);
                    return new AgentToolResult(callId, toolName, true, $"Sostituzione completata (tramite fuzzy line matching tollerante agli spazi) nel file: {relative}", DiffPatch: patch);
                }
            }
        }

        return new AgentToolResult(callId, toolName, false, string.Empty, $"TargetContent non trovato nel file {relative}. Usa read_file per leggere l'esatta sintassi delle righe di {relative} oppure usa write_file per riscrivere l'intero file.");
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

        // Prefer ripgrep (rg) for speed; fall back to linear scan if unavailable.
        var rgResult = TryRipgrepSearch(callId, toolName, query, targetDir, rootPath);
        if (rgResult is not null) return rgResult;

        return LinearGrepSearch(callId, toolName, query, targetDir, rootPath);
    }

    private static AgentToolResult? TryRipgrepSearch(string callId, string toolName, string query, string targetDir, string rootPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rg",
                // -n: line numbers, -i: case-insensitive, -m 50: max 50 matches, --no-heading, -l limited output
                Arguments = $"--no-heading -n -i -m 50 --max-count 50 {EscapeShellArg(query)} {EscapeShellArg(targetDir)}",
                WorkingDirectory = rootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10_000);

            // rg exits with 1 when no matches are found (not an error)
            if (proc.ExitCode > 1) return null;

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return new AgentToolResult(callId, toolName, true, $"No results found for '{query}'.");
            }

            // Relativize paths in ripgrep output
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    // rg output format: /abs/path/file:linenum:content
                    int firstColon = line.IndexOf(':');
                    if (firstColon > 0)
                    {
                        string absFile = line[..firstColon];
                        if (File.Exists(absFile))
                        {
                            string rel = Path.GetRelativePath(rootPath, absFile).Replace('\\', '/');
                            return rel + line[firstColon..];
                        }
                    }
                    return line;
                })
                .Take(50)
                .ToList();

            return new AgentToolResult(callId, toolName, true, string.Join("\n", lines));
        }
        catch
        {
            // ripgrep not available on this system
            return null;
        }
    }

    private static string EscapeShellArg(string arg)
    {
        // Wrap in quotes; escape internal quotes
        return $"\"{arg.Replace("\"", "\\\"")}\"";
    }

    private static AgentToolResult LinearGrepSearch(string callId, string toolName, string query, string targetDir, string rootPath)
    {
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
                // Skip unreadable files
            }

            if (results.Count >= 50) break;
        }

        if (results.Count == 0)
        {
            return new AgentToolResult(callId, toolName, true, $"No results found for '{query}'.");
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
            return new AgentToolResult(callId, toolName, true, $"Command started in background. TaskID: {taskInfo.TaskId}\nUse manage_task to poll status and logs.");
        }

        // Use PowerShell 7 (pwsh) as mandated by AGENTS.md — falls back to powershell.exe if pwsh is unavailable.
        string shellExe = ResolveShellExecutable();
        var psi = new ProcessStartInfo
        {
            FileName = shellExe,
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
            return new AgentToolResult(callId, toolName, false, string.Empty, "Cannot start shell process.");
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
            process.ExitCode == 0 ? null : $"Process exited with code {process.ExitCode}");
    }

    /// <summary>Resolves the best available PowerShell executable on this machine.</summary>
    private static string ResolveShellExecutable()
    {
        // Prefer PowerShell 7+ (pwsh) as mandated by AGENTS.md
        foreach (string candidate in new[] { "pwsh", "pwsh.exe" })
        {
            try
            {
                using var probe = new Process();
                probe.StartInfo = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-NoProfile -Command exit 0",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                if (probe.Start())
                {
                    probe.WaitForExit(3000);
                    if (probe.ExitCode == 0) return candidate;
                }
            }
            catch { }
        }
        return "powershell.exe"; // Legacy fallback
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

    private async Task<AgentToolResult> WebSearchAsync(
        string callId,
        string toolName,
        JsonElement args,
        CancellationToken cancellationToken)
    {
        string query = GetArgString(args, "query", "search", "q", "pattern") ?? "";
        string domain = GetArgString(args, "domain", "site", "source") ?? "";

        if (string.IsNullOrWhiteSpace(query))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Il parametro 'query' per la ricerca web è obbligatorio.");
        }

        string searchQuery = string.IsNullOrWhiteSpace(domain) ? query : $"{query} site:{domain}";
        logger?.LogInfo("AgentEngine", $"[WEB SEARCH] Query: '{searchQuery}'");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            string searchUrl = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(searchQuery)}";
            HttpResponseMessage resp = await http.GetAsync(searchUrl, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Ricerca web fallita con codice HTTP {(int)resp.StatusCode}");
            }

            string html = await resp.Content.ReadAsStringAsync(cancellationToken);
            var results = ParseDuckDuckGoSearchResults(html);

            if (results.Count == 0)
            {
                return new AgentToolResult(callId, toolName, true, $"Nessun risultato trovato per la ricerca web: '{searchQuery}'. Provare a riformulare la query.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[RISULTATI RICERCA WEB UFFICIALE: '{searchQuery}']\n");
            int idx = 1;
            foreach (var res in results.Take(6))
            {
                sb.AppendLine($"{idx}. **{res.Title}**");
                sb.AppendLine($"   URL: {res.Url}");
                sb.AppendLine($"   Estratto: {res.Snippet}\n");
                idx++;
            }

            return new AgentToolResult(callId, toolName, true, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            logger?.LogWarning("AgentEngine", $"Errore durante la ricerca web per '{searchQuery}': {ex.Message}");
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Impossibile completare la ricerca sul web: {ex.Message}");
        }
    }

    private static List<(string Title, string Url, string Snippet)> ParseDuckDuckGoSearchResults(string html)
    {
        var results = new List<(string Title, string Url, string Snippet)>();
        if (string.IsNullOrWhiteSpace(html)) return results;

        var matches = Regex.Matches(html, @"<a class=""result__a"" href=""([^""]+)""[^>]*>(.*?)</a>[\s\S]*?<a class=""result__snippet""[^>]*>(.*?)</a>", RegexOptions.Singleline);
        foreach (Match match in matches)
        {
            string rawUrl = match.Groups[1].Value;
            string rawTitle = match.Groups[2].Value;
            string rawSnippet = match.Groups[3].Value;

            string cleanTitle = Regex.Replace(rawTitle, "<.*?>", "").Trim();
            string cleanSnippet = Regex.Replace(rawSnippet, "<.*?>", "").Trim();
            cleanTitle = System.Net.WebUtility.HtmlDecode(cleanTitle);
            cleanSnippet = System.Net.WebUtility.HtmlDecode(cleanSnippet);

            string cleanUrl = rawUrl;
            var uddgMatch = Regex.Match(rawUrl, @"uddg=([^&]+)");
            if (uddgMatch.Success)
            {
                cleanUrl = Uri.UnescapeDataString(uddgMatch.Groups[1].Value);
            }

            if (!string.IsNullOrWhiteSpace(cleanTitle) && !string.IsNullOrWhiteSpace(cleanUrl))
            {
                results.Add((cleanTitle, cleanUrl, cleanSnippet));
            }
        }

        return results;
    }

    private async Task<AgentToolResult> InvokeSubagentAsync(
        string callId,
        string toolName,
        JsonElement args,
        string rootPath,
        Action<AgentStepEvent>? onStep,
        CancellationToken cancellationToken)
    {
        if (subagentRunner != null)
        {
            return await subagentRunner.InvokeSubagentAsync(callId, toolName, args, rootPath, onStep, cancellationToken);
        }

        logger?.LogWarning("AgentEngine", "[INVOKE_SUBAGENT] SubagentRunner is not configured.");
        return new AgentToolResult(
            callId,
            toolName,
            false,
            string.Empty,
            "invoke_subagent is not available because ISubagentRunner is not configured.");
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

    private async Task<AgentToolResult> MultiReplaceFileContentAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("Il parametro per il percorso del file ('relativePath' o 'path') è obbligatorio");

        string safePath = ResolveSafePathWithSmartFallback(rootPath, relative, out string actualRelative);
        if (!File.Exists(safePath))
        {
            string suggestions = GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File non trovato: {relative}.{suggestions}");
        }

        JsonElement chunksElem;
        if (!args.TryGetProperty("chunks", out chunksElem) && !args.TryGetProperty("replacements", out chunksElem))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Il parametro 'chunks' o 'replacements' (array di oggetti targetContent/replacementContent) è obbligatorio");
        }

        if (chunksElem.ValueKind != JsonValueKind.Array)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Il parametro 'chunks' deve essere un array JSON.");
        }

        string currentText = await File.ReadAllTextAsync(safePath, cancellationToken);
        int appliedCount = 0;
        var errors = new List<string>();

        foreach (var item in chunksElem.EnumerateArray())
        {
            string? target = GetArgString(item, "targetContent", "target", "oldContent", "search");
            string? replacement = GetArgString(item, "replacementContent", "replacement", "newContent", "replace");

            if (string.IsNullOrEmpty(target) || replacement is null)
            {
                errors.Add("Chunk con parametri targetContent/replacementContent non validi.");
                continue;
            }

            if (currentText.Contains(target))
            {
                currentText = currentText.Replace(target, replacement);
                appliedCount++;
            }
            else
            {
                string normOriginal = currentText.Replace("\r\n", "\n");
                string normTarget = target.Replace("\r\n", "\n");
                string normReplacement = replacement.Replace("\r\n", "\n");

                if (normOriginal.Contains(normTarget))
                {
                    currentText = normOriginal.Replace(normTarget, normReplacement);
                    if (currentText.Contains('\n') && !currentText.Contains("\r\n"))
                    {
                        currentText = currentText.Replace("\n", "\r\n");
                    }
                    appliedCount++;
                }
                else
                {
                    errors.Add($"TargetContent non trovato per il chunk: '{target.Substring(0, Math.Min(40, target.Length))}...'");
                }
            }
        }

        if (appliedCount > 0)
        {
            await File.WriteAllTextAsync(safePath, currentText, cancellationToken);
        }

        if (errors.Count > 0 && appliedCount == 0)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Nessun chunk applicato in {relative}. Dettagli:\n" + string.Join("\n", errors));
        }

        string msg = $"Applicati {appliedCount} chunk di sostituzione in {relative}.";
        if (errors.Count > 0)
        {
            msg += $" Avvisi:\n" + string.Join("\n", errors);
        }

        return new AgentToolResult(callId, toolName, true, msg);
    }

    private AgentToolResult GitDiffInspect(string callId, string toolName, JsonElement args, string rootPath)
    {
        string relative = GetArgString(args, "relativePath", "path") ?? "";
        string safePath = string.IsNullOrWhiteSpace(relative) ? rootPath : ResolveSafePath(rootPath, relative);

        try
        {
            var psiStatus = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --short",
                WorkingDirectory = safePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var pStatus = Process.Start(psiStatus);
            if (pStatus is null)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, "Impossibile avviare il processo Git.");
            }

            string statusOut = pStatus.StandardOutput.ReadToEnd();
            pStatus.WaitForExit();

            var psiDiff = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --stat",
                WorkingDirectory = safePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var pDiff = Process.Start(psiDiff);
            string diffOut = pDiff is not null ? pDiff.StandardOutput.ReadToEnd() : "";
            pDiff?.WaitForExit();

            var sb = new StringBuilder();
            sb.AppendLine($"[STATO GIT LOCAL WORKSPACE: {safePath}]");
            sb.AppendLine("==> Status (File Modificati / Non Tracciati):");
            sb.AppendLine(string.IsNullOrWhiteSpace(statusOut) ? "Workspace completamente pulito (nessuna modifica locale)." : statusOut.Trim());
            if (!string.IsNullOrWhiteSpace(diffOut))
            {
                sb.AppendLine("\n==> Statistica Diff Modifiche:");
                sb.AppendLine(diffOut.Trim());
            }

            return new AgentToolResult(callId, toolName, true, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Errore durante l'ispezione dello stato Git: {ex.Message}");
        }
    }

    private async Task<AgentToolResult> IngestOfficeDocAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = GetArgString(args, "relativePath", "path", "file", "filepath")
            ?? throw new ArgumentException("Il parametro 'relativePath' per il documento Office/PDF è obbligatorio");

        bool forceOcr = (args.TryGetProperty("forceOcr", out var f) && f.GetBoolean());
        string safePath = ResolveSafePath(rootPath, relative);

        if (!File.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Documento non trovato sul disco: {relative}");
        }

        if (ingestionService is not null)
        {
            try
            {
                var docInfo = new FileInfo(safePath);
                var doc = new ImportedDocument(
                    Id: 0,
                    DocumentUid: Guid.NewGuid().ToString("N"),
                    OriginalFileName: docInfo.Name,
                    OriginalPath: docInfo.FullName,
                    Sha256: null,
                    MimeType: "application/octet-stream",
                    FileExtension: docInfo.Extension,
                    FileSizeBytes: docInfo.Length,
                    Status: DocumentStatus.Imported,
                    PageCount: 0,
                    ChunkCount: 0,
                    CurrentJobId: null,
                    LastError: null,
                    CreatedAtUtc: DateTimeOffset.UtcNow,
                    UpdatedAtUtc: DateTimeOffset.UtcNow);

                var result = await ingestionService.IngestAsync(
                    doc,
                    checkpoint: null,
                    saveProgressAsync: (_, _) => Task.CompletedTask,
                    forceOcr: forceOcr,
                    cancellationToken: cancellationToken);

                string resJson = JsonSerializer.Serialize(new
                {
                    pageCount = result.PageCount,
                    chunkCount = result.ChunkCount,
                    fileName = docInfo.Name
                }, s_indentedOptions);

                return new AgentToolResult(callId, toolName, true, $"Ingestion Office/PDF completata per {relative}:\n{resJson}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Errore durante l'ingestion del documento {relative}: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Documento identificato per ingestion RAG: {relative} (IngestionService non registrato in questo contesto test)");
    }

    private async Task<AgentToolResult> GenerateImageOnnxAsync(string callId, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        string prompt = GetArgString(args, "prompt", "text", "description")
            ?? throw new ArgumentException("Il parametro 'prompt' per la generazione immagine è obbligatorio");

        string negativePrompt = GetArgString(args, "negativePrompt", "negative") ?? "";
        int width = GetArgInt(args, "width") ?? 512;
        int height = GetArgInt(args, "height") ?? 512;

        if (imageGenerationService is not null)
        {
            try
            {
                var req = new ImageGenerationRequest(
                    Prompt: prompt,
                    NegativePrompt: negativePrompt,
                    ModelId: null,
                    Width: width,
                    Height: height,
                    Steps: 20,
                    BatchSize: 1,
                    Seed: null);

                var resp = await imageGenerationService.GenerateAsync(req, cancellationToken);
                var generatedList = resp.Images.Select(img => new
                {
                    id = img.Id,
                    fileName = img.FileName,
                    mimeType = img.MimeType,
                    prompt = img.Prompt,
                    width = img.Width,
                    height = img.Height
                });

                string json = JsonSerializer.Serialize(new
                {
                    provider = resp.Provider,
                    message = resp.Message,
                    images = generatedList
                }, s_indentedOptions);

                return new AgentToolResult(callId, toolName, true, $"Immagine ONNX DirectML generata con successo:\n{json}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Impossibile generare immagine con ONNX DirectML: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Richiesta di generazione immagine ONNX simulata per prompt '{prompt}' (ImageGenerationService non disponibile nel runner)");
    }

    private async Task<AgentToolResult> QueryRetrievalIndexAsync(string callId, string toolName, JsonElement args, CancellationToken cancellationToken)
    {
        string query = GetArgString(args, "query", "q", "search")
            ?? throw new ArgumentException("Il parametro 'query' è obbligatorio");

        int topK = GetArgInt(args, "topK", "limit", "k") ?? 5;

        if (retrievalService is not null)
        {
            try
            {
                var searchReq = new DocumentSearchRequest(
                    Query: query,
                    DocumentIds: Array.Empty<long>(),
                    TopK: topK);

                var searchResp = await retrievalService.SearchAsync(searchReq, cancellationToken);
                float topScore = (float)(searchResp.Results.Count > 0 ? (searchResp.Results[0].ReRankScore ?? searchResp.Results[0].Score) : 0f);
                string cragConfidence = topScore >= 0.75f ? "HIGH (Correct)" : topScore >= 0.40f ? "MEDIUM (Ambiguous)" : "LOW (Incorrect)";

                var items = searchResp.Results.Select(r => new
                {
                    documentId = r.DocumentId,
                    documentName = r.DocumentName,
                    score = r.Score,
                    reRankScore = r.ReRankScore,
                    snippet = r.Snippet?.Substring(0, Math.Min(250, r.Snippet.Length)),
                    chunkId = r.ChunkId
                });

                string json = JsonSerializer.Serialize(new
                {
                    query = query,
                    totalMatches = searchResp.Results.Count,
                    cragConfidence = cragConfidence,
                    topReRankScore = topScore,
                    keywordBackend = searchResp.KeywordBackend,
                    vectorBackend = searchResp.VectorBackend,
                    results = items
                }, s_indentedOptions);

                return new AgentToolResult(callId, toolName, true, $"Risultati ricerca retrieval (SQLite FTS5 + Qdrant vectors | CRAG: {cragConfidence}):\n{json}");
            }
            catch (Exception ex)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, $"Errore durante la ricerca nel retrieval index: {ex.Message}");
            }
        }

        return new AgentToolResult(callId, toolName, true, $"Ricerca retrieval simulata per query '{query}' (HybridRetrievalService non registrato in questo contesto)");
    }

    private static string ResolveSafePath(string rootPath, string relativePath)
    {
        string root = Path.GetFullPath(rootPath);
        string cleanedRelative = (relativePath ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(cleanedRelative) && cleanedRelative.Contains(',') && !File.Exists(Path.Combine(root, cleanedRelative)))
        {
            cleanedRelative = cleanedRelative.Split(',')[0].Trim();
        }

        string target;
        if (Path.IsPathRooted(cleanedRelative))
        {
            target = Path.GetFullPath(cleanedRelative);
        }
        else
        {
            target = Path.GetFullPath(Path.Combine(root, cleanedRelative.TrimStart('/', '\\')));
        }

        string relFromRoot = Path.GetRelativePath(root, target);
        if (relFromRoot.StartsWith("..") || Path.IsPathRooted(relFromRoot))
        {
            throw new UnauthorizedAccessException($"Path Traversal bloccato: il percorso '{relativePath}' è all'esterno della cartella del workspace '{rootPath}'.");
        }

        return target;
    }

    private static string ResolveSafePathWithSmartFallback(string rootPath, string relativePath, out string resolvedRelativePath)
    {
        string safePath = ResolveSafePath(rootPath, relativePath);
        resolvedRelativePath = (relativePath ?? "").Trim().Replace('\\', '/');

        if (File.Exists(safePath) || Directory.Exists(safePath))
        {
            return safePath;
        }

        string? fileName = Path.GetFileName(relativePath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                var candidates = Directory.EnumerateFiles(rootPath, fileName, SearchOption.AllDirectories)
                    .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                    .ToList();

                if (candidates.Count > 0)
                {
                    string candidate = candidates[0];
                    resolvedRelativePath = Path.GetRelativePath(rootPath, candidate).Replace('\\', '/');
                    return candidate;
                }
            }
            catch
            {
                // Fallback safe
            }
        }

        return safePath;
    }

    private static string GetNearbyFileSuggestions(string rootPath, string relativePath)
    {
        string? fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;

        string ext = Path.GetExtension(fileName);
        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(nameWithoutExt)) return string.Empty;

        try
        {
            var suggestions = Directory.EnumerateFiles(rootPath, string.IsNullOrEmpty(ext) ? "*.*" : $"*{ext}", SearchOption.AllDirectories)
                .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
                .Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/'))
                .Where(rel => rel.Contains(nameWithoutExt, StringComparison.OrdinalIgnoreCase) || nameWithoutExt.Contains(Path.GetFileNameWithoutExtension(rel), StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            if (suggestions.Count > 0)
            {
                return $"\n[SUGGERIMENTO FILE REALI] File reali con nome o estensione simile trovati nel workspace:\n- " + string.Join("\n- ", suggestions);
            }
        }
        catch
        {
            // Safe fallback
        }

        return string.Empty;
    }

    private async Task<AgentToolResult> AstStructuralRefactorAsync(
        string callId,
        string toolName,
        JsonElement args,
        string rootPath,
        CancellationToken cancellationToken)
    {
        string operation = GetArgString(args, "operation", "op", "action", "mode") ?? "find_references";
        string targetSymbol = GetArgString(args, "targetSymbol", "symbol", "name", "target")
            ?? throw new ArgumentException("Il parametro 'targetSymbol' è obbligatorio per la rifattorizzazione strutturale AST");
        string relative = GetArgString(args, "relativePath", "path", "searchPath") ?? "";
        string newSymbolName = GetArgString(args, "newSymbolName", "newName", "replacementSymbol") ?? "";
        string newContent = GetArgString(args, "newContent", "replacement", "code") ?? "";

        string targetDir = ResolveSafePath(rootPath, relative);
        if (!Directory.Exists(targetDir) && File.Exists(targetDir))
        {
            targetDir = Path.GetDirectoryName(targetDir)!;
        }

        var codeFiles = Directory.EnumerateFiles(targetDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\.git\\") && !f.Contains("\\node_modules\\") && !f.Contains("\\bin\\") && !f.Contains("\\obj\\") &&
                        (f.EndsWith(".cs") || f.EndsWith(".ts") || f.EndsWith(".tsx") || f.EndsWith(".js") || f.EndsWith(".jsx") || f.EndsWith(".json")))
            .ToList();

        string pattern = $@"\b{Regex.Escape(targetSymbol)}\b";
        var regex = new Regex(pattern);

        if (operation.Equals("rename_symbol", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(newSymbolName))
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, "Il parametro 'newSymbolName' è obbligatorio per l'operazione 'rename_symbol'");
            }

            int modifiedFilesCount = 0;
            int totalReplacements = 0;

            foreach (var file in codeFiles)
            {
                string originalText = await File.ReadAllTextAsync(file, cancellationToken);
                if (regex.IsMatch(originalText))
                {
                    int matchesInFile = regex.Count(originalText);
                    string updatedText = regex.Replace(originalText, newSymbolName);
                    await File.WriteAllTextAsync(file, updatedText, cancellationToken);
                    modifiedFilesCount++;
                    totalReplacements += matchesInFile;
                }
            }

            string resultMsg = $"Rifattorizzazione simbolo '{targetSymbol}' -> '{newSymbolName}' completata: {totalReplacements} occorrenze sostituite in {modifiedFilesCount} file del workspace.";
            return new AgentToolResult(callId, toolName, true, resultMsg);
        }
        else if (operation.Equals("replace_symbol_body", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(newContent))
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, "Il parametro 'newContent' è obbligatorio per 'replace_symbol_body'");
            }

            foreach (var file in codeFiles)
            {
                string originalText = await File.ReadAllTextAsync(file, cancellationToken);
                if (regex.IsMatch(originalText))
                {
                    string updatedText = regex.Replace(originalText, newContent, 1);
                    await File.WriteAllTextAsync(file, updatedText, cancellationToken);
                    string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                    return new AgentToolResult(callId, toolName, true, $"Definizione/Corpo del simbolo '{targetSymbol}' sostituito con successo nel file {relFile}.");
                }
            }

            return new AgentToolResult(callId, toolName, false, string.Empty, $"Simbolo '{targetSymbol}' non trovato nei file di codice.");
        }
        else
        {
            var matchedLines = new List<string>();
            foreach (var file in codeFiles)
            {
                string[] lines = await File.ReadAllLinesAsync(file, cancellationToken);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                        matchedLines.Add($"{relFile}:{i + 1}: {lines[i].Trim()}");
                        if (matchedLines.Count >= 100) break;
                    }
                }
            }

            string resultText = matchedLines.Count > 0
                ? $"Trovati {matchedLines.Count} riferimenti per il simbolo '{targetSymbol}':\n" + string.Join("\n", matchedLines)
                : $"Nessun riferimento trovato per il simbolo '{targetSymbol}'.";

            return new AgentToolResult(callId, toolName, true, resultText);
        }
    }

    private AgentToolResult PlanTask(string callId, string toolName, JsonElement args)
    {
        var stepsList = new List<string>();
        if (args.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
        {
            int idx = 1;
            foreach (var item in stepsProp.EnumerateArray())
            {
                string desc = GetArgString(item, "description", "desc", "title", "text") ?? item.ToString();
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    stepsList.Add($"[ ] Step {idx++}: {desc}");
                }
            }
        }
        else if (args.TryGetProperty("plan", out var planProp))
        {
            string planText = planProp.GetString() ?? "";
            if (!string.IsNullOrWhiteSpace(planText))
            {
                stepsList.Add(planText);
            }
        }

        if (stepsList.Count == 0)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "Nessun passaggio fornito nell'argomento 'steps' o 'plan'.");
        }

        string planMarkdown = "### PIANO DI LAVORO AGENTE\n" + string.Join("\n", stepsList);
        return new AgentToolResult(callId, toolName, true, planMarkdown);
    }

    private AgentToolResult ReflectStep(string callId, string toolName, JsonElement args)
    {
        string stepId = GetArgString(args, "stepId", "step_id", "id") ?? "1";
        string status = GetArgString(args, "status", "state", "result") ?? "completed";
        string learnings = GetArgString(args, "learnings", "reflection", "notes", "findings") ?? "Step verificato con successo.";

        string summary = $"[SELF-REFLECTION STEP {stepId}] Esito: {status.ToUpperInvariant()}\nAnalisi e Apprendimenti: {learnings}";
        return new AgentToolResult(callId, toolName, true, summary);
    }
}
