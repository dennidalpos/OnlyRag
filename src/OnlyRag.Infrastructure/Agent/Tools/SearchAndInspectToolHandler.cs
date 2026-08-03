using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class SearchAndInspectToolHandler : IToolHandler
{
    public bool CanHandle(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "grep_search" or "git_diff_inspect" => true,
            _ => false
        };
    }

    public Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        AgentToolResult res = toolName.ToLowerInvariant() switch
        {
            "grep_search" => GrepSearch(callId, toolName, args, workspaceRoot),
            "git_diff_inspect" => GitDiffInspect(callId, toolName, args, workspaceRoot),
            _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool '{toolName}' not supported by SearchAndInspectToolHandler")
        };
        return Task.FromResult(res);
    }

    private AgentToolResult GrepSearch(string callId, string toolName, JsonElement args, string rootPath)
    {
        string query = ToolHelper.GetArgString(args, "query", "pattern", "search", "text")
            ?? throw new ArgumentException("The 'query' parameter is required");

        string relative = ToolHelper.GetArgString(args, "searchPath", "path", "directory", "dir", "relativePath") ?? "";

        string targetDir = ToolHelper.ResolveSafePath(rootPath, relative);
        if (!Directory.Exists(targetDir) && File.Exists(targetDir))
        {
            targetDir = Path.GetDirectoryName(targetDir)!;
        }

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
                Arguments = $"--no-heading -n -i -m 50 --max-count 50 {EscapeShellArg(query)} {EscapeShellArg(targetDir)}",
                WorkingDirectory = rootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            if (!proc.WaitForExit(10_000))
            {
                try { proc.Kill(true); } catch { }
            }

            if (proc.ExitCode > 1) return null;

            string stdout = stdoutTask.Result;
            if (string.IsNullOrWhiteSpace(stdout))
            {
                return new AgentToolResult(callId, toolName, true, $"No results found for '{query}'.");
            }

            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
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
            return null;
        }
    }

    private static string EscapeShellArg(string arg)
    {
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

    private AgentToolResult GitDiffInspect(string callId, string toolName, JsonElement args, string rootPath)
    {
        string relative = ToolHelper.GetArgString(args, "relativePath", "path") ?? "";
        string safePath = string.IsNullOrWhiteSpace(relative) ? rootPath : ToolHelper.ResolveSafePath(rootPath, relative);

        try
        {
            var psiStatus = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "-c core.pager=cat status --short",
                WorkingDirectory = safePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psiStatus.EnvironmentVariables["PAGER"] = "cat";
            psiStatus.EnvironmentVariables["GIT_PAGER"] = "cat";

            using var pStatus = Process.Start(psiStatus);
            if (pStatus is null)
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, "Unable to start Git process.");
            }

            var statusOutTask = pStatus.StandardOutput.ReadToEndAsync();
            var statusErrTask = pStatus.StandardError.ReadToEndAsync();
            Task.WaitAll(statusOutTask, statusErrTask);
            if (!pStatus.WaitForExit(5000))
            {
                try { pStatus.Kill(true); } catch { }
            }
            string statusOut = statusOutTask.Result;

            var psiDiff = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "-c core.pager=cat diff --stat",
                WorkingDirectory = safePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psiDiff.EnvironmentVariables["PAGER"] = "cat";
            psiDiff.EnvironmentVariables["GIT_PAGER"] = "cat";

            using var pDiff = Process.Start(psiDiff);
            string diffOut = "";
            if (pDiff is not null)
            {
                var diffOutTask = pDiff.StandardOutput.ReadToEndAsync();
                var diffErrTask = pDiff.StandardError.ReadToEndAsync();
                Task.WaitAll(diffOutTask, diffErrTask);
                if (!pDiff.WaitForExit(5000))
                {
                    try { pDiff.Kill(true); } catch { }
                }
                diffOut = diffOutTask.Result;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[GIT LOCAL WORKSPACE STATUS: {safePath}]");
            sb.AppendLine("==> Status (Modified / Untracked Files):");
            sb.AppendLine(string.IsNullOrWhiteSpace(statusOut) ? "Workspace completely clean (no local modifications)." : statusOut.Trim());
            if (!string.IsNullOrWhiteSpace(diffOut))
            {
                sb.AppendLine("\n==> Diff Modifications Statistic:");
                sb.AppendLine(diffOut.Trim());
            }

            return new AgentToolResult(callId, toolName, true, sb.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, $"Error inspecting Git status: {ex.Message}");
        }
    }
}
