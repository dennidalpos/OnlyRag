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
    private readonly ILoggingService? logger;

    public WorkspaceToolExecutor(
        BackgroundTaskManager taskManager,
        IHybridRetrievalService? retrievalService = null,
        IDocumentIngestionService? ingestionService = null,
        ImageGenerationService? imageGenerationService = null,
        ISubagentRunner? subagentRunner = null,
        IWorkspaceVectorIndexerService? vectorIndexer = null,
        IAstDependencyGraphService? astGraphService = null,
        ILoggingService? logger = null)
    {
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

        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
        {
            string err = "No authorized project folder found on the system.";
            logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] {err}");
            return new AgentToolResult(callId, toolName, false, string.Empty, err);
        }

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson, s_jsonDocOptions);
            var root = doc.RootElement;

            var handler = handlers.FirstOrDefault(h => h.CanHandle(toolName));
            if (handler is null)
            {
                string err = $"Unrecognized tool: {toolName}";
                logger?.LogWarning("AgentEngine", $"[TOOL EXEC FAIL] {err}");
                return new AgentToolResult(callId, toolName, false, string.Empty, err);
            }

            AgentToolResult result = await handler.ExecuteAsync(callId, toolName, root, workspaceRoot, onStep, cancellationToken);

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
