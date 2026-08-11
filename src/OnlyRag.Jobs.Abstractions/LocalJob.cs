namespace OnlyRag.Jobs.Abstractions;

public sealed record LocalJob(
    string Id,
    string Type,
    JobStatus Status,
    int Priority,
    int ProgressPercent,
    string CurrentStep,
    string PayloadJson,
    string CheckpointJson,
    string? Error,
    int RetryCount,
    int MaxRetries,
    DateTimeOffset? NextAttemptAtUtc,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
