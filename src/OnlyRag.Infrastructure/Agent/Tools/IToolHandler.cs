using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Tools;

public interface IToolHandler
{
    bool CanHandle(string toolName);

    Task<AgentToolResult> ExecuteAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default);
}
