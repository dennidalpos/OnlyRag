using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent;

public record SubagentExecutionResult(
    string Role,
    bool Success,
    string Output,
    string Error,
    IReadOnlyList<string> KeyFacts,
    IReadOnlyList<string> ModifiedFiles
);

public interface ISubagentRunner
{
    Task<AgentToolResult> InvokeSubagentAsync(
        string callId,
        string toolName,
        JsonElement args,
        string workspaceRoot,
        Action<AgentStepEvent>? onStep = null,
        CancellationToken cancellationToken = default);
}

