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

        var tasks = subagentRequests.Select(async spec =>
        {
            return await RunSingleSubagentAsync(spec, workspaceRoot, currentDepth + 1, onStep, cancellationToken);
        }).ToList();

        var results = await Task.WhenAll(tasks);

        bool allSucceeded = results.All(r => r.Success);
        var combinedOutput = new StringBuilder();

        foreach (var res in results)
        {
            if (combinedOutput.Length > 0) combinedOutput.AppendLine("\n" + new string('-', 40) + "\n");
            combinedOutput.AppendLine(res.Output);
        }

        string resultOutput = combinedOutput.ToString();
        string? resultError = allSucceeded ? null : string.Join("; ", results.Where(r => !r.Success).Select(r => r.Error));

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
            var engine = scope.ServiceProvider.GetRequiredService<AgentLoopEngine>();

            bool isReadOnlyRole = spec.Role.Contains("research", StringComparison.OrdinalIgnoreCase) ||
                                  spec.Role.Contains("explore", StringComparison.OrdinalIgnoreCase) ||
                                  spec.Role.Contains("inspect", StringComparison.OrdinalIgnoreCase) ||
                                  spec.Role.Contains("audit", StringComparison.OrdinalIgnoreCase);

            string mode = isReadOnlyRole ? "ask" : "write";
            string roleInstruction = isReadOnlyRole
                ? "[SUBAGENT ROLE DIRECTIVE - RESEARCHER & EXPLORER]\nYou are a specialized read-only research and exploration subagent. Focus on inspection, ripgrep, and web search. Do NOT modify files."
                : $"[SUBAGENT ROLE DIRECTIVE - {spec.Role.ToUpperInvariant()}]\nYou are an autonomous subagent focused on this specific sub-goal. Execute the necessary actions and produce a clear report of results.";

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
        int maxIter = GetIntProp(elem, "max_iterations", "maxIterations", "MaxIterations") ?? DefaultSubagentMaxIterations;

        return new SubagentSpec(role, prompt, model, maxIter);
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

    private record SubagentSpec(string Role, string Prompt, string? Model, int MaxIterations);
}
