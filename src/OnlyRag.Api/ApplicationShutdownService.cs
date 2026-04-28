using OnlyRag.Core;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class ApplicationShutdownService
{
    private static readonly TimeSpan DefaultRunningJobStopTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);

    private readonly ILocalJobQueue jobs;
    private readonly RunningJobCancellationRegistry cancellationRegistry;
    private readonly InProcessBackendDescriptor descriptor;

    public ApplicationShutdownService(
        ILocalJobQueue jobs,
        RunningJobCancellationRegistry cancellationRegistry,
        InProcessBackendDescriptor descriptor)
    {
        this.jobs = jobs;
        this.cancellationRegistry = cancellationRegistry;
        this.descriptor = descriptor;
    }

    public async Task<AppShutdownPreparationResponse> PrepareAsync(CancellationToken cancellationToken = default)
    {
        return await PrepareAsync(DefaultRunningJobStopTimeout, cancellationToken);
    }

    internal async Task<AppShutdownPreparationResponse> PrepareAsync(
        TimeSpan runningJobStopTimeout,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LocalJob> jobList = await jobs.ListAsync(cancellationToken: cancellationToken);
        LocalJob[] activeJobs = jobList
            .Where(job => job.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Paused)
            .ToArray();

        foreach (LocalJob job in activeJobs)
        {
            await jobs.CancelAsync(job.Id, cancellationToken);
            cancellationRegistry.Cancel(job.Id);
        }

        string[] runningJobIds = activeJobs
            .Where(job => job.Status is JobStatus.Running)
            .Select(job => job.Id)
            .ToArray();
        string[] unstoppedJobIds = await WaitForRunningJobsToStopAsync(
            runningJobIds,
            runningJobStopTimeout,
            cancellationToken);

        BackendLog.Write(
            descriptor.StoragePaths,
            $"Shutdown preparation cancelled {activeJobs.Length} active job(s); unstopped running job(s): {unstoppedJobIds.Length}.");

        return new AppShutdownPreparationResponse(
            activeJobs.Length,
            activeJobs.Length,
            unstoppedJobIds);
    }

    private async Task<string[]> WaitForRunningJobsToStopAsync(
        IReadOnlyList<string> runningJobIds,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (runningJobIds.Count == 0)
        {
            return [];
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        HashSet<string> expected = new(runningJobIds, StringComparer.Ordinal);
        while (DateTimeOffset.UtcNow < deadline)
        {
            string[] stillRunning = cancellationRegistry
                .ListRunningJobIds()
                .Where(expected.Contains)
                .ToArray();
            if (stillRunning.Length == 0)
            {
                return [];
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return cancellationRegistry
            .ListRunningJobIds()
            .Where(expected.Contains)
            .ToArray();
    }
}
