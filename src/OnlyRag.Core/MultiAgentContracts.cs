namespace OnlyRag.Core;

public enum MultiAgentSubtaskStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

public sealed record MultiAgentSubtask(
    string SubtaskId,
    string Role,
    string Goal,
    IReadOnlyList<string> DependsOnSubtaskIds,
    MultiAgentSubtaskStatus Status,
    string? Output = null,
    string? Error = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null);

public sealed record InterAgentMessage(
    string MessageId,
    string SenderRole,
    string RecipientRole,
    string MessageText,
    DateTimeOffset SentAtUtc);

public sealed record MultiAgentOrchestrationRequest(
    string OverallGoal,
    string? WorkspaceRoot = null,
    string? Model = null);

public sealed record MultiAgentOrchestrationStatus(
    string OrchestrationId,
    string OverallGoal,
    bool IsCompleted,
    bool HasFailed,
    IReadOnlyList<MultiAgentSubtask> Subtasks,
    IReadOnlyList<InterAgentMessage> Messages,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc = null);

public interface IMultiAgentOrchestratorService
{
    Task<MultiAgentOrchestrationStatus> StartOrchestrationAsync(
        MultiAgentOrchestrationRequest request,
        CancellationToken cancellationToken = default);

    Task<MultiAgentOrchestrationStatus?> GetStatusAsync(
        string orchestrationId,
        CancellationToken cancellationToken = default);
}

public sealed record CriticEvaluationResult(
    bool Approved,
    float Score,
    string Feedback,
    IReadOnlyList<string> IdentifiedDefects,
    DateTimeOffset EvaluatedAtUtc);

public sealed record MultiAgentRoleConfig(
    string Role,
    string Specialty,
    int TokenBudget,
    int ToolCallBudget,
    IReadOnlyList<string> PermittedTools);

