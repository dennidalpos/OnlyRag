using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class RefactorAndPlanningToolHandler : IToolHandler
{
    private readonly IAstDependencyGraphService? astGraphService;

    public RefactorAndPlanningToolHandler(IAstDependencyGraphService? astGraphService = null)
    {
        this.astGraphService = astGraphService;
    }

    public bool CanHandle(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "plan_task" or "create_plan" or "update_plan" or
            "reflect_step" or "reflect" or "self_reflection" or
            "ast_structural_refactor" or "refactor_symbol" => true,
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
            "plan_task" or "create_plan" or "update_plan" => PlanTask(callId, toolName, args),
            "reflect_step" or "reflect" or "self_reflection" => ReflectStep(callId, toolName, args),
            "ast_structural_refactor" or "refactor_symbol" => await AstStructuralRefactorAsync(callId, toolName, args, workspaceRoot, cancellationToken),
            _ => new AgentToolResult(callId, toolName, false, string.Empty, $"Tool '{toolName}' not supported by RefactorAndPlanningToolHandler")
        };
    }

    private AgentToolResult PlanTask(string callId, string toolName, JsonElement args)
    {
        var stepsList = new List<string>();
        if (args.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
        {
            int idx = 1;
            foreach (var item in stepsProp.EnumerateArray())
            {
                string desc = ToolHelper.GetArgString(item, "description", "desc", "title", "text") ?? item.ToString();
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
            return new AgentToolResult(callId, toolName, false, string.Empty, "No steps provided in the 'steps' or 'plan' argument.");
        }

        string planMarkdown = "### AGENT WORK PLAN\n" + string.Join("\n", stepsList);
        return new AgentToolResult(callId, toolName, true, planMarkdown);
    }

    private AgentToolResult ReflectStep(string callId, string toolName, JsonElement args)
    {
        string stepId = ToolHelper.GetArgString(args, "stepId", "step_id", "id") ?? "1";
        string status = ToolHelper.GetArgString(args, "status", "state", "result") ?? "completed";
        string learnings = ToolHelper.GetArgString(args, "learnings", "reflection", "notes", "findings") ?? "Step successfully verified.";

        string summary = $"[SELF-REFLECTION STEP {stepId}] Result: {status.ToUpperInvariant()}\nAnalysis and Learnings: {learnings}";
        return new AgentToolResult(callId, toolName, true, summary);
    }

    private async Task<AgentToolResult> AstStructuralRefactorAsync(
        string callId,
        string toolName,
        JsonElement args,
        string rootPath,
        CancellationToken cancellationToken)
    {
        string operation = ToolHelper.GetArgString(args, "operation", "op", "action", "mode") ?? "find_references";
        string relative = ToolHelper.GetArgString(args, "relativePath", "path", "searchPath") ?? "";
        string targetSymbol = ToolHelper.GetArgString(args, "targetSymbol", "symbol", "name", "target") ?? "";
        string newSymbolName = ToolHelper.GetArgString(args, "newSymbolName", "newName", "replacementSymbol") ?? "";
        string newContent = ToolHelper.GetArgString(args, "newContent", "replacement", "code") ?? "";

        if (operation.Equals("query_ast_graph", StringComparison.OrdinalIgnoreCase) || operation.Equals("graph", StringComparison.OrdinalIgnoreCase))
        {
            if (astGraphService != null)
            {
                var graphResult = await astGraphService.AnalyzeWorkspaceDependenciesAsync(rootPath, relative, cancellationToken);
                string summary = $"AST dependencies analysis completed for '{graphResult.TargetFile}': {graphResult.DirectDependencies.Count} direct dependencies, {graphResult.DependentFiles.Count} dependent files.\n\n{graphResult.MermaidDiagram}";
                return new AgentToolResult(callId, toolName, true, summary);
            }
            return new AgentToolResult(callId, toolName, false, string.Empty, "The AST graph analysis service (IAstDependencyGraphService) is not available.");
        }

        if (string.IsNullOrWhiteSpace(targetSymbol) && !operation.Equals("extract_symbol_outline", StringComparison.OrdinalIgnoreCase) && !operation.Equals("outline", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "The 'targetSymbol' parameter is required for the requested operation.");
        }

        string targetDir = ToolHelper.ResolveSafePath(rootPath, relative);
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
                return new AgentToolResult(callId, toolName, false, string.Empty, "The 'newSymbolName' parameter is required for the 'rename_symbol' operation");
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

            string resultMsg = $"Symbol refactoring '{targetSymbol}' -> '{newSymbolName}' completed: {totalReplacements} occurrences replaced in {modifiedFilesCount} workspace files.";
            return new AgentToolResult(callId, toolName, true, resultMsg);
        }
        else if (operation.Equals("replace_symbol_body", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(newContent))
            {
                return new AgentToolResult(callId, toolName, false, string.Empty, "The 'newContent' parameter is required for 'replace_symbol_body'");
            }

            foreach (var file in codeFiles)
            {
                string originalText = await File.ReadAllTextAsync(file, cancellationToken);
                if (regex.IsMatch(originalText))
                {
                    string updatedText = regex.Replace(originalText, newContent, 1);
                    await File.WriteAllTextAsync(file, updatedText, cancellationToken);
                    string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                    return new AgentToolResult(callId, toolName, true, $"Definition/Body of symbol '{targetSymbol}' replaced successfully in file {relFile}.");
                }
            }

            return new AgentToolResult(callId, toolName, false, string.Empty, $"Simbolo '{targetSymbol}' not found in code files.");
        }
        else if (operation.Equals("extract_symbol_definition", StringComparison.OrdinalIgnoreCase))
        {
            var matchedDefinitions = new List<string>();
            var defPattern = new Regex($@"(?:class|interface|struct|enum|record|function|const|let|var|public|private|internal|protected|sealed)\s+{Regex.Escape(targetSymbol)}\b");

            foreach (var file in codeFiles)
            {
                string text = await File.ReadAllTextAsync(file, cancellationToken);
                var match = defPattern.Match(text);
                if (match.Success)
                {
                    string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                    int startIdx = Math.Max(0, match.Index);
                    int endIdx = Math.Min(text.Length, match.Index + match.Length + 350);
                    string snippet = text.Substring(startIdx, endIdx - startIdx);
                    matchedDefinitions.Add($"File: {relFile}\n{snippet.Trim()}");
                }
            }

            string res = matchedDefinitions.Count > 0
                ? $"Definitions found for symbol '{targetSymbol}':\n\n" + string.Join("\n\n---\n\n", matchedDefinitions)
                : $"No symbol declaration/definition found for '{targetSymbol}'.";

            return new AgentToolResult(callId, toolName, true, res);
        }
        else if (operation.Equals("extract_symbol_outline", StringComparison.OrdinalIgnoreCase) || operation.Equals("outline", StringComparison.OrdinalIgnoreCase))
        {
            var outlinePattern = new Regex(@"^\s*(?:public|private|protected|internal|sealed|abstract|async|static|export|default|class|interface|struct|enum|record|function|type)\s+([A-Za-z0-9_<>,\s\(\)\:\?]+)", RegexOptions.Multiline);
            var fileOutlines = new List<string>();

            foreach (var file in codeFiles.Take(15))
            {
                string[] lines = await File.ReadAllLinesAsync(file, cancellationToken);
                string relFile = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                var fileSymbols = new List<string>();

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (outlinePattern.IsMatch(line) && !line.Trim().StartsWith("//") && !line.Trim().StartsWith('*'))
                    {
                        fileSymbols.Add($"  L{i + 1}: {line.Trim()}");
                        if (fileSymbols.Count >= 20) break;
                    }
                }

                if (fileSymbols.Count > 0)
                {
                    fileOutlines.Add($"📄 **{relFile}**\n" + string.Join("\n", fileSymbols));
                }
            }

            string outlineText = fileOutlines.Count > 0
                ? $"Symbol Structure / AST Outline ({fileOutlines.Count} files):\n\n" + string.Join("\n\n", fileOutlines)
                : "No significant symbol identified for outline.";

            return new AgentToolResult(callId, toolName, true, outlineText);
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
                ? $"Found {matchedLines.Count} references for symbol '{targetSymbol}':\n" + string.Join("\n", matchedLines)
                : $"No references found for symbol '{targetSymbol}'.";

            return new AgentToolResult(callId, toolName, true, resultText);
        }
    }
}
