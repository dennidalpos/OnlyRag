using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Api;

public sealed class SubagentRunner : ISubagentRunner
{
    private static readonly AsyncLocal<int> s_nestingDepth = new();
    private const int MaxNestingDepth = 3;
    private const int DefaultSubagentMaxIterations = 20;

    private readonly IServiceProvider serviceProvider;
    private readonly ILoggingService? logger;

    public SubagentRunner(IServiceProvider serviceProvider, ILoggingService? logger = null)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public async Task<AgentToolResult> InvokeSubagentAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        int currentDepth = s_nestingDepth.Value;
        if (currentDepth >= MaxNestingDepth)
        {
            string depthErr = $"[SUBAGENT RECURSION BLOCKED] Reached maximum subagent nesting depth limit ({MaxNestingDepth}). Subagents cannot spawn further nested subagents.";
            logger?.LogWarning("SubagentRunner", depthErr);
            return new AgentToolResult(callId, toolName, false, string.Empty, depthErr);
        }

        var subagentRequests = ParseSubagentSpecs(args);
        if (subagentRequests.Count == 0)
        {
            string parseErr = "No valid prompt or subagent specification provided in parameters. Specify 'prompt' (string) or 'subagents' (array).";
            logger?.LogWarning("SubagentRunner", parseErr);
            return new AgentToolResult(callId, toolName, false, string.Empty, parseErr);
        }

        logger?.LogInfo("SubagentRunner", $"[SUBAGENT ORCHESTRATOR] Launching {subagentRequests.Count} subagent(s) at nesting depth {currentDepth + 1}.");

        var executionMap = new Dictionary<string, SubagentExecutionResult>(StringComparer.OrdinalIgnoreCase);
        var completedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remainingSpecs = new List<SubagentSpec>(subagentRequests);
        var allResults = new List<SubagentExecutionResult>();

        while (remainingSpecs.Count > 0)
        {
            // Select subagents whose dependencies have all completed
            var readyBatch = remainingSpecs
                .Where(s => s.DependsOn.Count == 0 || s.DependsOn.All(dep => completedRoles.Contains(dep)))
                .ToList();

            if (readyBatch.Count == 0)
            {
                // Unresolvable dependency or cycle detected; fallback to execute remaining concurrently
                logger?.LogWarning("SubagentRunner", "[SUBAGENT DAG WARNING] Unresolved dependency or cycle detected in subagent DAG. Executing remaining subagents concurrently.");
                readyBatch = remainingSpecs.ToList();
            }

            foreach (var s in readyBatch) remainingSpecs.Remove(s);

            var batchTasks = readyBatch.Select(async spec =>
            {
                // Inject parent outputs into prompt if dependent on previous subagents
                string enrichedPrompt = spec.Prompt;
                if (spec.DependsOn.Count > 0)
                {
                    var parentOutputs = new StringBuilder("\n\n### [PARENT SUBAGENT CONTEXT INJECTION]\n");
                    foreach (var depRole in spec.DependsOn)
                    {
                        if (executionMap.TryGetValue(depRole, out var parentRes))
                        {
                            parentOutputs.AppendLine($"#### Output from parent subagent [{depRole}]:");
                            parentOutputs.AppendLine(parentRes.Output);
                            if (parentRes.KeyFacts.Count > 0)
                            {
                                parentOutputs.AppendLine($"Key Facts: {string.Join("; ", parentRes.KeyFacts)}");
                            }
                            parentOutputs.AppendLine();
                        }
                    }
                    enrichedPrompt += parentOutputs.ToString();
                }

                var specToRun = spec with { Prompt = enrichedPrompt };
                return await RunSingleSubagentAsync(specToRun, workspaceRoot, currentDepth + 1, onStep, cancellationToken);
            }).ToList();

            var batchResults = await Task.WhenAll(batchTasks);

            foreach (var res in batchResults)
            {
                executionMap[res.Role] = res;
                completedRoles.Add(res.Role);
                allResults.Add(res);
            }
        }

        bool allSucceeded = allResults.All(r => r.Success);
        var combinedOutput = new StringBuilder();

        foreach (var res in allResults)
        {
            if (combinedOutput.Length > 0) combinedOutput.AppendLine("\n" + new string('-', 40) + "\n");
            combinedOutput.AppendLine(res.Output);
        }

        string resultOutput = combinedOutput.ToString();
        string? resultError = allSucceeded ? null : string.Join("; ", allResults.Where(r => !r.Success).Select(r => r.Error));

        return new AgentToolResult(
            callId,
            toolName,
            allSucceeded,
            resultOutput,
            resultError ?? string.Empty);
    }

    private async Task<SubagentExecutionResult> RunSingleSubagentAsync(
        SubagentSpec spec,
        string workspaceRoot,
        int nextDepth,
        Action<AgentStepEvent>? onStep,
        CancellationToken cancellationToken)
    {
        s_nestingDepth.Value = nextDepth;
        logger?.LogInfo("SubagentRunner", $"[SUBAGENT START] Role: '{spec.Role}', Prompt: '{spec.Prompt}'");

        var keyFacts = new List<string>();
        var modifiedFiles = new List<string>();

        try
        {
            using var scope = serviceProvider.CreateScope();
            var cacheRepo = scope.ServiceProvider.GetService<OnlyRag.Infrastructure.Agent.Memory.ISubagentReportCacheRepository>();
            if (cacheRepo != null)
            {
                try
                {
                    var cached = await cacheRepo.GetCachedReportAsync(spec.Role, spec.Prompt, workspaceRoot, cancellationToken);
                    if (cached != null)
                    {
                        logger?.LogInfo("SubagentRunner", $"[SUBAGENT CACHE HIT] Role: '{spec.Role}', PromptHash: '{cached.PromptHash}'");
                        return new SubagentExecutionResult(spec.Role, true, cached.ReportMarkdown, string.Empty, cached.KeyFacts, cached.ModifiedFiles);
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning("SubagentRunner", $"Subagent cache check failed: {ex.Message}");
                }
            }

            var engine = scope.ServiceProvider.GetRequiredService<AgentLoopEngine>();

            string roleLower = spec.Role.ToLowerInvariant();
            bool isReadOnlyRole = roleLower.Contains("research") || roleLower.Contains("explore") || roleLower.Contains("inspect") || roleLower.Contains("audit");
            string mode = isReadOnlyRole ? "ask" : "write";

            string roleInstruction = roleLower switch
            {
                var r when r.Contains("architect") =>
                    "[SUBAGENT ROLE DIRECTIVE - ARCHITECT]\nYou are a senior system architect subagent. Focus on high-level system decomposition, API contract design, layer boundaries, and structural dependency planning. Provide clear architectural blueprints and recommendations.",
                var r when r.Contains("research") || r.Contains("explore") || r.Contains("inspect") || r.Contains("audit") =>
                    "[SUBAGENT ROLE DIRECTIVE - RESEARCH & EXPLORATION]\nYou are a specialized read-only research subagent. Focus on codebase search, ripgrep inspection, documentation review, and web search. Do NOT modify workspace code files.",
                var r when r.Contains("refactor") || r.Contains("clean") =>
                    "[SUBAGENT ROLE DIRECTIVE - REFACTOR & CLEANUP]\nYou are a code refactoring subagent. Focus on eliminating technical debt, simplifying complex methods, preserving existing public API contracts, and ensuring clean AST symbol renaming.",
                var r when r.Contains("verifier") || r.Contains("test") =>
                    "[SUBAGENT ROLE DIRECTIVE - VERIFIER & TESTER]\nYou are a QA verification subagent. Focus on running build scripts, unit tests, static linter checks, and validating overall code health.",
                _ => $"[SUBAGENT ROLE DIRECTIVE - {spec.Role.ToUpperInvariant()}]\nYou are an autonomous subagent focused on this specific sub-goal. Execute the necessary actions and produce a clear report of results."
            };

            var runRequest = new AgentRunRequest(
                Goal: $"{roleInstruction}\n\n[ASSIGNED OBJECTIVE]\n{spec.Prompt}",
                WorkspaceRoot: workspaceRoot,
                AutoApproveCommands: true,
                MaxIterations: Math.Min(spec.MaxIterations, 30),
                Model: spec.Model,
                Mode: mode);

            var outputSb = new StringBuilder();
            string? finalResponse = null;
            var recentThoughts = new StringBuilder();

            await foreach (var stepEvent in engine.RunAgentLoopAsync(runRequest, cancellationToken))
            {
                var taggedEvent = stepEvent with { SubagentRole = spec.Role };
                onStep?.Invoke(taggedEvent);

                if (stepEvent.Type == "final_response" && !string.IsNullOrWhiteSpace(stepEvent.Content))
                {
                    finalResponse = stepEvent.Content;
                }
                else if (stepEvent.Type == "thought_chunk" && !string.IsNullOrWhiteSpace(stepEvent.Content))
                {
                    recentThoughts.Append(stepEvent.Content);
                }
                else if (stepEvent.Type == "tool_result" && stepEvent.ToolResult != null)
                {
                    if (stepEvent.ToolResult.Success && (stepEvent.ToolResult.ToolName.Contains("write") || stepEvent.ToolResult.ToolName.Contains("replace")))
                    {
                        modifiedFiles.Add(stepEvent.ToolResult.ToolName);
                    }
                    if (stepEvent.ToolResult.Success && stepEvent.ToolResult.ToolName == "reflect_step")
                    {
                        keyFacts.Add(stepEvent.ToolResult.Output);
                    }
                }
                else if (stepEvent.Type == "error" && !string.IsNullOrWhiteSpace(stepEvent.Content))
                {
                    logger?.LogWarning("SubagentRunner", $"[SUBAGENT STEP ERROR] Role: '{spec.Role}', Error: {stepEvent.Content}");
                }
            }

            string agentOutput = finalResponse
                ?? (recentThoughts.Length > 0 ? recentThoughts.ToString() : "Subagent completed without explicit output text.");

            string formattedResult = $"### [SUBAGENT OUTPUT: {spec.Role}]\n\n{agentOutput}";
            logger?.LogInfo("SubagentRunner", $"[SUBAGENT COMPLETE] Role: '{spec.Role}' finished execution.");

            if (cacheRepo != null && !string.IsNullOrWhiteSpace(formattedResult))
            {
                try
                {
                    string promptHash = OnlyRag.Infrastructure.Agent.Memory.SqliteSubagentReportCacheRepository.ComputeHash($"{spec.Role.Trim().ToLowerInvariant()}:{spec.Prompt.Trim()}:{workspaceRoot.Trim().ToLowerInvariant()}");
                    var cacheEntry = new OnlyRag.Infrastructure.Agent.Memory.CachedSubagentReport(
                        spec.Role,
                        promptHash,
                        workspaceRoot,
                        formattedResult,
                        keyFacts,
                        modifiedFiles,
                        DateTimeOffset.UtcNow);
                    await cacheRepo.SaveCachedReportAsync(cacheEntry, CancellationToken.None);
                }
                catch { }
            }

            return new SubagentExecutionResult(spec.Role, true, formattedResult, string.Empty, keyFacts, modifiedFiles);
        }
        catch (Exception ex)
        {
            string err = $"Error executing subagent '{spec.Role}': {ex.Message}";
            logger?.LogError("SubagentRunner", err, ex);
            return new SubagentExecutionResult(spec.Role, false, $"### [SUBAGENT FAILED: {spec.Role}]\n\n{err}", err, keyFacts, modifiedFiles);
        }
    }


    private static List<SubagentSpec> ParseSubagentSpecs(JsonElement args)
    {
        var list = new List<SubagentSpec>();

        // Scenario 1: Array "subagents" or "Subagents"
        JsonElement arrayElem = default;
        if (args.TryGetProperty("subagents", out arrayElem) || args.TryGetProperty("Subagents", out arrayElem))
        {
            if (arrayElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in arrayElem.EnumerateArray())
                {
                    var spec = ExtractSpecFromElement(elem);
                    if (spec != null) list.Add(spec);
                }
            }
        }

        // Scenario 2: Single subagent spec directly in root object
        if (list.Count == 0)
        {
            var spec = ExtractSpecFromElement(args);
            if (spec != null) list.Add(spec);
        }

        return list;
    }

    private static SubagentSpec? ExtractSpecFromElement(JsonElement elem)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        string? prompt = GetStringProp(elem, "prompt", "Prompt", "goal", "Goal", "task", "Task");
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        string role = GetStringProp(elem, "role", "Role", "typeName", "TypeName", "subagent", "Subagent") ?? "Subagent";
        string? model = GetStringProp(elem, "model", "Model");
        string? workspace = GetStringProp(elem, "workspace", "Workspace", "mode", "Mode");
        int maxIter = GetIntProp(elem, "max_iterations", "maxIterations", "MaxIterations") ?? DefaultSubagentMaxIterations;
        var dependsOn = GetStringListProp(elem, "dependsOn", "depends_on", "DependsOn");

        return new SubagentSpec(role, prompt, model, maxIter, workspace, dependsOn);
    }

    private static string? GetStringProp(JsonElement elem, params string[] props)
    {
        foreach (var prop in props)
        {
            if (elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            {
                return val.GetString();
            }
        }
        return null;
    }

    private static IReadOnlyList<string> GetStringListProp(JsonElement elem, params string[] props)
    {
        var list = new List<string>();
        foreach (var prop in props)
        {
            if (elem.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in val.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                        {
                            list.Add(item.GetString()!.Trim());
                        }
                    }
                    if (list.Count > 0) return list;
                }
                else if (val.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(val.GetString()))
                {
                    list.Add(val.GetString()!.Trim());
                    return list;
                }
            }
        }
        return list;
    }

    private static int? GetIntProp(JsonElement elem, params string[] props)
    {
        foreach (var prop in props)
        {
            if (elem.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.Number && val.TryGetInt32(out int i))
            {
                return i;
            }
        }
        return null;
    }

    private record SubagentSpec(string Role, string Prompt, string? Model, int MaxIterations, string? Workspace = null, IReadOnlyList<string>? DependsOn = null)
    {
        public IReadOnlyList<string> DependsOn { get; init; } = DependsOn ?? Array.Empty<string>();
    }
}
