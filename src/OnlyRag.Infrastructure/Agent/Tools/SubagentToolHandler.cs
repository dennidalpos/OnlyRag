using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Infrastructure.Agent.Tools;

public sealed class SubagentToolHandler : IToolHandler
{
    private readonly ISubagentRunner? subagentRunner;
    private readonly ILoggingService? logger;

    public SubagentToolHandler(ISubagentRunner? subagentRunner = null, ILoggingService? logger = null)
    {
        this.subagentRunner = subagentRunner;
        this.logger = logger;
    }

    public bool CanHandle(string toolName)
    {
        return toolName.Equals("invoke_subagent", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default)
    {
        if (subagentRunner != null)
        {
            return await subagentRunner.InvokeSubagentAsync(callId, toolName, args, workspaceRoot, onStep, cancellationToken);
        }

        logger?.LogWarning("AgentEngine", "[INVOKE_SUBAGENT] SubagentRunner is not configured.");
        return new AgentToolResult(
            callId,
            toolName,
            false,
            string.Empty,
            "invoke_subagent is not available because ISubagentRunner is not configured.");
    }
}
