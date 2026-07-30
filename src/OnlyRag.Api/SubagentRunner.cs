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
            string parseErr = "Nessun prompt o specifica di subagente valida fornita nei parametri. Specificare 'prompt' (stringa) o 'subagents' (array).";
            logger?.LogWarning("SubagentRunner", parseErr);
            return new AgentToolResult(callId, toolName, false, string.Empty, parseErr);
        }

        logger?.LogInfo("SubagentRunner", $"[SUBAGENT ORCHESTRATOR] Avvio di {subagentRequests.Count} subagente/i a livello di profondità {currentDepth + 1}.");

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

    private async Task<(bool Success, string Output, string Error)> RunSingleSubagentAsync(
        SubagentSpec spec,
        string workspaceRoot,
        int nextDepth,
        Action<AgentStepEvent>? onStep,
        CancellationToken cancellationToken)
    {
        s_nestingDepth.Value = nextDepth;
        logger?.LogInfo("SubagentRunner", $"[SUBAGENT START] Role: '{spec.Role}', Prompt: '{spec.Prompt}'");

        try
        {
            using var scope = serviceProvider.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<AgentLoopEngine>();

            var runRequest = new AgentRunRequest(
                Goal: $"[SUBAGENT ROLE: {spec.Role}]\n\n{spec.Prompt}",
                WorkspaceRoot: workspaceRoot,
                AutoApproveCommands: true,
                MaxIterations: Math.Min(spec.MaxIterations, 30),
                Model: spec.Model,
                Mode: "write");

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
                else if (stepEvent.Type == "error" && !string.IsNullOrWhiteSpace(stepEvent.Content))
                {
                    logger?.LogWarning("SubagentRunner", $"[SUBAGENT STEP ERROR] Role: '{spec.Role}', Error: {stepEvent.Content}");
                }
            }

            string agentOutput = finalResponse
                ?? (recentThoughts.Length > 0 ? recentThoughts.ToString() : "Subagente completato senza testo esplicito.");

            string formattedResult = $"### [SUBAGENT OUTPUT: {spec.Role}]\n\n{agentOutput}";
            logger?.LogInfo("SubagentRunner", $"[SUBAGENT COMPLETE] Role: '{spec.Role}' ha completato l'esecuzione.");
            return (true, formattedResult, string.Empty);
        }
        catch (Exception ex)
        {
            string err = $"Errore nell'esecuzione del subagente '{spec.Role}': {ex.Message}";
            logger?.LogError("SubagentRunner", err, ex);
            return (false, $"### [SUBAGENT FAILED: {spec.Role}]\n\n{err}", err);
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
