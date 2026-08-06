namespace OnlyRag.Core;

public enum BatchJobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public sealed record BatchFileItem(
    string FileId,
    string FilePath,
    long SizeBytes,
    BatchJobStatus Status,
    string? ErrorMessage = null,
    int RetryAttempt = 0);

public sealed record BatchIngestionJob(
    string BatchId,
    IReadOnlyList<BatchFileItem> Files,
    BatchJobStatus Status,
    int CompletedCount,
    int TotalCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? FinishedAtUtc = null);

public sealed record BatchIngestionProgressEvent(
    string BatchId,
    string FileId,
    string FilePath,
    int CompletedCount,
    int TotalCount,
    BatchJobStatus FileStatus,
    string? ErrorMessage = null,
    int RetryAttempt = 0);

public interface IBatchIngestionQueueService
{
    Task<BatchIngestionJob> EnqueueBatchAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default);
    Task<BatchIngestionJob?> GetBatchStatusAsync(string batchId, CancellationToken cancellationToken = default);
    Task CancelBatchAsync(string batchId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<BatchIngestionProgressEvent> SubscribeProgressAsync(string batchId, CancellationToken cancellationToken = default);
}
