using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent.Tools;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Agent;

public sealed class WorkspaceToolExecutor
{
    private static readonly JsonDocumentOptions s_jsonDocOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly List<IToolHandler> handlers;
    private readonly IAgentExecutionPolicyService? policyService;
    private readonly ILoggingService? logger;

    public WorkspaceToolExecutor(
        BackgroundTaskManager taskManager,
        IHybridRetrievalService? retrievalService = null,
        IDocumentIngestionService? ingestionService = null,
        ImageGenerationService? imageGenerationService = null,
        ISubagentRunner? subagentRunner = null,
        IWorkspaceVectorIndexerService? vectorIndexer = null,
        IAstDependencyGraphService? astGraphService = null,
        IAgentExecutionPolicyService? policyService = null,
        ILoggingService? logger = null)
    {
        this.policyService = policyService;
        this.logger = logger;
        this.handlers = new List<IToolHandler>
        {
            new FileSystemToolHandler(vectorIndexer),
            new SearchAndInspectToolHandler(),
            new TaskAndCommandToolHandler(taskManager),
            new ExternalServicesToolHandler(retrievalService, ingestionService, imageGenerationService, logger),
            new RefactorAndPlanningToolHandler(astGraphService),
            new SubagentToolHandler(subagentRunner, logger)
        };
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

        ToolExecutionContext context = new(callId, toolName, argumentsJson, workspaceRoot);
        if (policyService != null)
        {
            AgentPolicyDecision decision = await policyService.EvaluateAsync(context, cancellationToken);
            if (!decision.Allowed)
            {
                string err = $"Policy violation ({decision.RiskLevel}): {decision.DenialReason}";
                logger?.LogWarning("AgentEngine", $"[POLICY DENIED] Tool: '{toolName}', CallID: '{callId}', Reason: {err}");
                return new AgentToolResult(callId, toolName, false, string.Empty, err);
            }
        }

        bool requiresWorkspace = IsWorkspaceFolderRequired(toolName);
        if (requiresWorkspace && (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot)))
        {
            string err = "Nessuna cartella di progetto selezionata. Seleziona una cartella di progetto prima di accedere ai file o eseguire comandi su disco.";
            logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] {err}");
            return new AgentToolResult(callId, toolName, false, string.Empty, err);
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson, s_jsonDocOptions);
            var root = doc.RootElement;

            string normName = toolName.ToLowerInvariant();
            if (normName is "parallel_tool_calls" or "parallel_tools" or "batch_tool_calls" or "tool_calls")
            {
                return await HandleParallelToolCallsAsync(callId, toolName, root, workspaceRoot, onStep, cancellationToken);
            }

            var handler = handlers.FirstOrDefault(h => h.CanHandle(toolName));
            if (handler is null)
            {
                string err = $"Unrecognized tool: {toolName}";
                logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] {err}");
                return new AgentToolResult(callId, toolName, false, string.Empty, err);
            }

            AgentToolResult result = await handler.ExecuteAsync(callId, toolName, root, workspaceRoot ?? string.Empty, onStep, cancellationToken);

            if (policyService != null)
            {
                await policyService.PostExecutionVerifyAsync(context, result.Success, result.Output, result.Error, cancellationToken);
            }

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
            string err = $"Error executing tool '{toolName}': {ex.Message}";
            logger?.LogError("AgentEngine", err, ex);
            return new AgentToolResult(callId, toolName, false, string.Empty, err);
        }
    }

    private async Task<AgentToolResult> HandleParallelToolCallsAsync(
        string callId,
        string toolName,
        JsonElement root,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep,
        CancellationToken cancellationToken)
    {
        JsonElement items = default;
        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root;
        }
        else if (root.TryGetProperty("calls", out var callsElem) && callsElem.ValueKind == JsonValueKind.Array)
        {
            items = callsElem;
        }
        else if (root.TryGetProperty("tools", out var toolsElem) && toolsElem.ValueKind == JsonValueKind.Array)
        {
            items = toolsElem;
        }
        else if (root.TryGetProperty("tool_calls", out var tcElem) && tcElem.ValueKind == JsonValueKind.Array)
        {
            items = tcElem;
        }
        else if (root.TryGetProperty("parallel_tool_calls", out var ptElem) && ptElem.ValueKind == JsonValueKind.Array)
        {
            items = ptElem;
        }

        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
        {
            return new AgentToolResult(callId, toolName, false, string.Empty, "parallel_tool_calls requires a JSON array of tool invocation objects.");
        }

        var outputs = new List<string>();
        var errors = new List<string>();
        int index = 0;

        foreach (var item in items.EnumerateArray())
        {
            index++;
            string? subTool = ToolHelper.GetArgString(item, "tool", "name", "tool_name", "function", "action");
            if (string.IsNullOrWhiteSpace(subTool))
            {
                errors.Add($"[Call #{index}] Tool name missing");
                continue;
            }

            string subArgs = "{}";
            if (item.TryGetProperty("arguments", out var argElem))
            {
                subArgs = argElem.GetRawText();
            }
            else if (item.TryGetProperty("args", out argElem))
            {
                subArgs = argElem.GetRawText();
            }
            else if (item.TryGetProperty("parameters", out argElem))
            {
                subArgs = argElem.GetRawText();
            }

            string subCallId = $"{callId}_{index}";
            AgentToolResult subRes = await ExecuteToolAsync(subCallId, subTool, subArgs, workspaceRoot, onStep, cancellationToken);
            if (subRes.Success)
            {
                outputs.Add($"[{subTool}]: {subRes.Output}");
            }
            else
            {
                errors.Add($"[{subTool} Error]: {subRes.Error}");
            }
        }

        bool success = errors.Count == 0;
        string combinedOutput = string.Join("\n\n", outputs);
        string combinedError = string.Join("\n\n", errors);

        return new AgentToolResult(callId, toolName, success, combinedOutput, combinedError);
    }

    private static bool IsWorkspaceFolderRequired(string toolName)
    {
        string name = toolName.ToLowerInvariant();
        return name switch
        {
            "plan_task" or "create_plan" or "update_plan" or
            "reflect_step" or "reflect" or "self_reflection" or
            "web_search" or "search_web" or
            "query_retrieval_index" or "search_vector_index" or
            "generate_image_onnx" or "generate_image" or
            "invoke_subagent" or "spawn_subagent" or
            "manage_task" => false,
            _ => true
        };
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
}
