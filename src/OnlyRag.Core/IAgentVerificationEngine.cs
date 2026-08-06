namespace OnlyRag.Core;

public interface IAgentVerificationEngine
{
    Task<AgentVerificationEvidence> VerifyStepAsync(
        AgentTaskStep step,
        AgentToolCall toolCall,
        AgentToolResult toolResult,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyPlanCompletionAsync(
        AgentTypedPlan plan,
        IReadOnlyList<AgentVerificationEvidence> evidences,
        CancellationToken cancellationToken = default);
}
