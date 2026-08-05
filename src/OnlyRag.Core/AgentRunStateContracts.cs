namespace OnlyRag.Core;

public enum AgentRunPhase
{
    Plan,
    Act,
    Observe,
    Verify,
    Recover,
    Finalize,
    Completed,
    Failed,
    Cancelled
}

public sealed record AgentRunBudget(
    int MaxToolCalls = 100,
    int MaxEstimatedTokens = 60_000,
    TimeSpan? MaxDuration = null)
{
    public TimeSpan EffectiveMaxDuration => MaxDuration ?? TimeSpan.FromMinutes(30);
}

/// <summary>Supported runtime-verifiable forms of an agent goal's completion criteria.</summary>
public enum AgentCompletionVerificationKind
{
    Command,
    Tool
}

/// <summary>
/// A required, machine-verifiable outcome for an agent run. A criterion is satisfied only by a
/// successful tool execution observed by the runtime; model prose and self-reflection are not evidence.
/// </summary>
public sealed record AgentCompletionCriterion(
    string Id,
    string Description,
    AgentCompletionVerificationKind VerificationKind,
    string ExpectedToolName,
    string? ExpectedCommand = null,
    bool Required = true);

public enum AgentCompletionVerificationStatus
{
    Pending,
    Passed,
    Failed
}

public sealed record AgentCompletionVerification(
    string CriterionId,
    AgentCompletionVerificationStatus Status,
    string ToolCallId,
    string ToolName,
    string Evidence,
    DateTimeOffset VerifiedAtUtc);

public sealed record AgentRunSnapshot(
    string RunId,
    string Goal,
    string Mode,
    string? Model,
    string WorkspaceRoot,
    AgentRunPhase Phase,
    AgentRunBudget Budget,
    int ToolCallsUsed,
    int EstimatedTokensUsed,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? LastError,
    string? FinalResponse,
    IReadOnlyList<string> Messages,
    IReadOnlyList<AgentCompletionCriterion>? CompletionCriteria = null,
    IReadOnlyList<AgentCompletionVerification>? CompletionVerifications = null)
{
    public IReadOnlyList<AgentCompletionCriterion> EffectiveCompletionCriteria => CompletionCriteria ?? [];
    public IReadOnlyList<AgentCompletionVerification> EffectiveCompletionVerifications => CompletionVerifications ?? [];
}

public sealed record AgentRunTransition(
    string RunId,
    AgentRunPhase From,
    AgentRunPhase To,
    string Reason,
    DateTimeOffset OccurredAtUtc);

/// <summary>Immutable, runtime-produced audit event for an agent decision or observed result.</summary>
public sealed record AgentRunTraceEvent(
    long Id,
    string RunId,
    int Step,
    string EventType,
    DateTimeOffset OccurredAtUtc,
    AgentRunPhase Phase,
    string? Decision = null,
    string? ToolName = null,
    string? ToolCallId = null,
    bool? Success = null,
    string? Observation = null,
    string? Error = null,
    int? EstimatedTokens = null,
    int? ToolCallsUsed = null,
    double? LatencyMs = null,
    string? Evidence = null,
    string? Outcome = null);

public sealed record AgentRunEvaluationSummary(
    string RunId,
    bool Succeeded,
    bool Regressed,
    TimeSpan Duration,
    int StepCount,
    int ToolCallCount,
    int ErrorCount,
    int EstimatedTokens,
    double ModelLatencyMs,
    DateTimeOffset EvaluatedAtUtc);

public interface IAgentRunStateRepository
{
    Task<AgentRunSnapshot> CreateAsync(AgentRunSnapshot run, CancellationToken cancellationToken = default);
    Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken cancellationToken = default);
    Task SaveAsync(AgentRunSnapshot run, CancellationToken cancellationToken = default);
    Task AppendTransitionAsync(AgentRunTransition transition, CancellationToken cancellationToken = default);
    Task AppendTraceEventAsync(AgentRunTraceEvent traceEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRunTraceEvent>> ListTraceEventsAsync(string runId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentRunSnapshot>> GetResumableAsync(CancellationToken cancellationToken = default);
}
