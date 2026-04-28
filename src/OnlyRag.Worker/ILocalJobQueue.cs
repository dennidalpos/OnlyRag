namespace OnlyRag.Worker;

public interface ILocalJobQueue
{
    Task<LocalJob> CreateAsync(CreateLocalJobRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalJob>> ListAsync(int? limit = null, CancellationToken cancellationToken = default);

    Task<LocalJob?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalJob?> CancelAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalJob?> PauseAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalJob?> ResumeAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalJob?> SaveCheckpointAsync(
        string id,
        LocalJobCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    Task<int> RecoverInterruptedJobsAsync(CancellationToken cancellationToken = default);

    Task<LocalJob?> TryLeaseNextAsync(CancellationToken cancellationToken = default);

    Task<LocalJob?> CompleteAsync(string id, CancellationToken cancellationToken = default);

    Task<LocalJob?> FailAsync(
        string id,
        string error,
        bool retryable,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<int> PurgeCompletedAsync(CancellationToken cancellationToken = default);
}
