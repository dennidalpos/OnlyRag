using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class FileSystemToolHandler : IToolHandler
{
    private static readonly JsonSerializerOptions s_indentedOptions = new() { WriteIndented = true };
    private readonly IWorkspaceVectorIndexerService? vectorIndexer;

    public FileSystemToolHandler(IWorkspaceVectorIndexerService? vectorIndexer = null)
    {
        this.vectorIndexer = vectorIndexer;
    }

    public bool CanHandle(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "list_dir" or "read_file" or "view_file" or "write_file" or "write_to_file" or
            "replace_file_content" or "multi_replace_file_content" => true,
            _ => false
        };
    }

    public async Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        return toolName.ToLowerInvariant() switch
        {
            "list_dir" => ListDir(callId, toolName, args, workspaceRoot),
            "read_file" or "view_file" => await ReadFileAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            "write_file" or "write_to_file" => await WriteFileAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            "replace_file_content" => await ReplaceFileContentAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            "multi_replace_file_content" => await MultiReplaceFileContentAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool '{toolName}' not supported by FileSystemToolHandler")
        };
    }

    private AgentToolResult ListDir(string callId, string toolName, JsonElement args, string rootPath)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target") ?? "";
        string safePath = ToolHelper.ResolveSafePath(rootPath, relative);

        if (!Directory.Exists(safePath))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Directory not found: {relative}");
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
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("The file path parameter ('relativePath' or 'path') is required");

        int? startLine = ToolHelper.GetArgInt(args, "startLine", "start", "fromLine");
        int? endLine = ToolHelper.GetArgInt(args, "endLine", "end", "toLine");

        string safePath = ToolHelper.ResolveSafePathWithSmartFallback(rootPath, relative, out _);
        if (!File.Exists(safePath))
        {
            string suggestions = ToolHelper.GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File not found: {relative}.{suggestions}");
        }

        string[] lines = await File.ReadAllLinesAsync(safePath, cancellationToken);
        if (startLine.HasValue || endLine.HasValue)
        {
            int start = Math.Max(1, startLine ?? 1) - 1;
            int end = Math.Min(lines.Length, endLine ?? lines.Length);

            if (start >= lines.Length)
            {
                return new AgentToolResult(callId, toolName, true, $"[File has only {lines.Length} lines]");
            }

            var sliced = lines.Skip(start).Take(end - start).Select((line, idx) => $"{start + idx + 1}: {line}");
            return new AgentToolResult(callId, toolName, true, string.Join("\n", sliced));
        }

        if (lines.Length > 800)
        {
            var truncated = lines.Take(800).Select((line, idx) => $"{idx + 1}: {line}");
            string output = string.Join("\n", truncated) + $"\n\n... [File truncated, showing 800 lines of {lines.Length} total]";
            return new AgentToolResult(callId, toolName, true, output);
        }

        var numberedLines = lines.Select((line, idx) => $"{idx + 1}: {line}");
        return new AgentToolResult(callId, toolName, true, string.Join("\n", numberedLines));
    }

    private async Task<AgentToolResult> WriteFileAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("The file path parameter ('relativePath' or 'path') is required");

        string content = ToolHelper.GetArgString(args, "content", "text", "code", "fileContent") ?? "";

        string safePath = ToolHelper.ResolveSafePath(rootPath, relative);
        string? parent = Path.GetDirectoryName(safePath);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        string original = File.Exists(safePath) ? await File.ReadAllTextAsync(safePath, cancellationToken) : string.Empty;
        await File.WriteAllTextAsync(safePath, content, cancellationToken);
        _ = vectorIndexer?.IndexWorkspaceFileAsync(rootPath, relative, cancellationToken);
        string patch = ToolHelper.GenerateUnifiedDiffPatch(relative, original, content);
        return new AgentToolResult(callId, toolName, true, $"File saved successfully: {relative} ({content.Length} characters)", DiffPatch: patch);
    }

    private async Task<AgentToolResult> ReplaceFileContentAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("The file path parameter ('relativePath' or 'path') is required");

        string target = ToolHelper.GetArgString(args, "targetContent", "target", "oldContent", "old_string", "search")
            ?? throw new ArgumentException("The 'targetContent' parameter is required");

        string replacement = ToolHelper.GetArgString(args, "replacementContent", "replacement", "newContent", "new_string", "replace")
            ?? throw new ArgumentException("The 'replacementContent' parameter is required");

        int? startLine = ToolHelper.GetArgInt(args, "startLine", "start", "fromLine");
        int? endLine = ToolHelper.GetArgInt(args, "endLine", "end", "toLine");

        string safePath = ToolHelper.ResolveSafePathWithSmartFallback(rootPath, relative, out _);
        if (!File.Exists(safePath))
        {
            string suggestions = ToolHelper.GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File not found: {relative}.{suggestions}");
        }

        string original = await File.ReadAllTextAsync(safePath, cancellationToken);

        if (startLine.HasValue || endLine.HasValue)
        {
            string[] lines = original.Replace("\r\n", "\n").Split('\n');
            int startIdx = Math.Max(0, (startLine ?? 1) - 1);
            int endIdx = Math.Min(lines.Length, endLine ?? lines.Length);

            if (startIdx < lines.Length && startIdx < endIdx)
            {
                string targetRangeText = string.Join("\n", lines.Skip(startIdx).Take(endIdx - startIdx));
                string normTarget = target.Replace("\r\n", "\n");
                string normReplacement = replacement.Replace("\r\n", "\n");

                if (targetRangeText.Contains(normTarget))
                {
                    string updatedRangeText = targetRangeText.Replace(normTarget, normReplacement);
                    var newLinesList = new List<string>(lines.Take(startIdx))
                    {
                        updatedRangeText
                    };
                    newLinesList.AddRange(lines.Skip(endIdx));
                    string updated = string.Join("\n", newLinesList);
                    if (original.Contains("\r\n")) updated = updated.Replace("\n", "\r\n");

                    await File.WriteAllTextAsync(safePath, updated, cancellationToken);
                    string patch = ToolHelper.GenerateUnifiedDiffPatch(relative, original, updated);
                    return new AgentToolResult(callId, toolName, true, $"Targeted replacement (line range {startIdx + 1}-{endIdx}) completed successfully in file: {relative}", DiffPatch: patch);
                }
            }
        }

        if (original.Contains(target))
        {
            string updated = original.Replace(target, replacement);
            await File.WriteAllTextAsync(safePath, updated, cancellationToken);
            string patch = ToolHelper.GenerateUnifiedDiffPatch(relative, original, updated);
            return new AgentToolResult(callId, toolName, true, $"Replacement completed successfully in file: {relative}", DiffPatch: patch);
        }

        string normOriginal = original.Replace("\r\n", "\n");
        string normTargetMain = target.Replace("\r\n", "\n");
        string normReplacementMain = replacement.Replace("\r\n", "\n");

        if (normOriginal.Contains(normTargetMain))
        {
            string updated = normOriginal.Replace(normTargetMain, normReplacementMain);
            if (original.Contains("\r\n"))
            {
                updated = updated.Replace("\n", "\r\n");
            }
            await File.WriteAllTextAsync(safePath, updated, cancellationToken);
            string patch = ToolHelper.GenerateUnifiedDiffPatch(relative, original, updated);
            return new AgentToolResult(callId, toolName, true, $"Replacement completed (with line ending normalization) in file: {relative}", DiffPatch: patch);
        }

        string[] origLines = normOriginal.Split('\n');
        string[] targetLines = normTargetMain.Split('\n');

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
                    var newLinesList = new List<string>(origLines.Take(i))
                    {
                        normReplacementMain
                    };
                    newLinesList.AddRange(origLines.Skip(i + targetLines.Length));
                    string updated = string.Join("\n", newLinesList);
                    if (original.Contains("\r\n"))
                    {
                        updated = updated.Replace("\n", "\r\n");
                    }
                    await File.WriteAllTextAsync(safePath, updated, cancellationToken);
                    string patch = ToolHelper.GenerateUnifiedDiffPatch(relative, original, updated);
                    return new AgentToolResult(callId, toolName, true, $"Replacement completed (via space-tolerant fuzzy line matching) in file: {relative}", DiffPatch: patch);
                }
            }
        }

        return new AgentToolResult(callId, toolName, false, string.Empty, $"TargetContent not found in file {relative}.. Use read_file to read the exact syntax of the lines in {relative} or use write_file to rewrite the entire file.");
    }

    private async Task<AgentToolResult> MultiReplaceFileContentAsync(string callId, string toolName, JsonElement args, string rootPath, CancellationToken cancellationToken)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "file", "filepath", "filename", "target")
            ?? throw new ArgumentException("The file path parameter ('relativePath' or 'path') is required");

        string safePath = ToolHelper.ResolveSafePathWithSmartFallback(rootPath, relative, out _);
        if (!File.Exists(safePath))
        {
            string suggestions = ToolHelper.GetNearbyFileSuggestions(rootPath, relative);
            return new AgentToolResult(callId, toolName, false, string.Empty, $"File not found: {relative}.{suggestions}");
        }

        if (!args.TryGetProperty("chunks", out var chunksElem) && !args.TryGetProperty("replacements", out chunksElem))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "The 'chunks' or 'replacements' parameter (array of targetContent/replacementContent objects) is required");
        }

        if (chunksElem.ValueKind != JsonValueKind.Array)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "The 'chunks' parameter must be a JSON array.");
        }

        string currentText = await File.ReadAllTextAsync(safePath, cancellationToken);
        int appliedCount = 0;
        var errors = new List<string>();

        foreach (var item in chunksElem.EnumerateArray())
        {
            string? target = ToolHelper.GetArgString(item, "targetContent", "target", "oldContent", "search");
            string? replacement = ToolHelper.GetArgString(item, "replacementContent", "replacement", "newContent", "replace");

            if (string.IsNullOrEmpty(target) || replacement is null)
            {
                errors.Add("Chunk with invalid targetContent/replacementContent parameters.");
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
                    errors.Add($"TargetContent not found for chunk: '{target[..Math.Min(40, target.Length)]}...'");
                }
            }
        }

        if (appliedCount > 0)
        {
            await File.WriteAllTextAsync(safePath, currentText, cancellationToken);
        }

        if (errors.Count > 0 && appliedCount == 0)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"No chunks applied in {relative}. Details:\n" + string.Join("\n", errors));
        }

        string msg = $"Applied {appliedCount} replacement chunks in {relative}.";
        if (errors.Count > 0)
        {
            msg += $" Warnings:\n" + string.Join("\n", errors);
        }

        return new AgentToolResult(callId, toolName, true, msg);
    }
}
