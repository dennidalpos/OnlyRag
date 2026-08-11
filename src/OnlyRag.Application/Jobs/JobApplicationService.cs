using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Application.Jobs;

public sealed class JobApplicationService
{
    private readonly ILocalJobQueue queue;
    private readonly RunningJobCancellationRegistry cancellationRegistry;

    public JobApplicationService(
        ILocalJobQueue queue,
        RunningJobCancellationRegistry cancellationRegistry)
    {
        this.queue = queue;
        this.cancellationRegistry = cancellationRegistry;
    }

    public Task<IReadOnlyList<LocalJob>> ListAsync(int? limit, CancellationToken cancellationToken) =>
        queue.ListAsync(limit, cancellationToken);

    public Task<LocalJob?> GetAsync(string id, CancellationToken cancellationToken) =>
        queue.GetAsync(id, cancellationToken);

    public Task<LocalJob> CreateAsync(
        CreateLocalJobRequest request,
        CancellationToken cancellationToken) =>
        queue.CreateAsync(request, cancellationToken);

    public async Task<LocalJob?> CancelAsync(string id, CancellationToken cancellationToken)
    {
        LocalJob? job = await queue.CancelAsync(id, cancellationToken);
        if (job is not null)
        {
            cancellationRegistry.Cancel(id);
        }

        return job;
    }

    public async Task<LocalJob?> PauseAsync(string id, CancellationToken cancellationToken)
    {
        LocalJob? job = await queue.PauseAsync(id, cancellationToken);
        if (job is not null)
        {
            cancellationRegistry.Cancel(id);
        }

        return job;
    }

    public async Task<JobResumeResult> ResumeAsync(string id, CancellationToken cancellationToken)
    {
        LocalJob? current = await queue.GetAsync(id, cancellationToken);
        if (current is null)
        {
            return JobResumeResult.NotFound;
        }

        if (current.Status is JobStatus.Pausing || cancellationRegistry.IsRunning(id))
        {
            return JobResumeResult.PauseInProgress;
        }

        LocalJob? job = await queue.ResumeAsync(id, cancellationToken);
        return job is null ? JobResumeResult.NotFound : new JobResumeResult(job, null);
    }

    public async Task<JobDeleteResult> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        LocalJob? job = await queue.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return JobDeleteResult.NotFound;
        }

        if (job.Status is JobStatus.Running or JobStatus.Pausing or JobStatus.Pending)
        {
            return JobDeleteResult.Active;
        }

        return await queue.DeleteAsync(id, cancellationToken)
            ? JobDeleteResult.Deleted
            : JobDeleteResult.NotFound;
    }

    public Task<int> PurgeCompletedAsync(CancellationToken cancellationToken) =>
        queue.PurgeCompletedAsync(cancellationToken);

    public async Task CancelAndWaitAsync(
        string? jobId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        LocalJob? currentJob = await queue.GetAsync(jobId, cancellationToken);
        if (currentJob?.Status is not (JobStatus.Pending or JobStatus.Running or JobStatus.Pausing))
        {
            return;
        }

        await queue.CancelAsync(jobId, cancellationToken);
        cancellationRegistry.Cancel(jobId);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            while (cancellationRegistry.IsRunning(jobId))
            {
                await Task.Delay(80, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Il job {jobId} non si e fermato entro 10 secondi. Riprovare.");
        }
    }
}

public sealed record JobResumeResult(LocalJob? Job, string? ConflictCode)
{
    public static JobResumeResult NotFound { get; } = new(null, "not_found");
    public static JobResumeResult PauseInProgress { get; } = new(null, "job_pause_in_progress");
}

public enum JobDeleteResult
{
    Deleted,
    NotFound,
    Active
}
