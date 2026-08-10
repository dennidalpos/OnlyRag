using Microsoft.Extensions.Hosting;
using OnlyRag.Worker;

namespace OnlyRag.Api;

internal sealed class LocalJobWorkerService : BackgroundService
{
    private readonly ILocalJobQueue queue;
    private readonly LocalJobQueueDescriptor descriptor;
    private readonly IReadOnlyDictionary<string, ILocalJobHandler> handlers;
    private readonly RunningJobCancellationRegistry cancellationRegistry;
    private readonly IPerformanceSettingsService performanceSettings;
    private readonly InProcessBackendDescriptor backendDescriptor;
    private readonly OnlyRag.Core.IHardwareMonitorService? hardwareMonitor;

    public LocalJobWorkerService(
        ILocalJobQueue queue,
        LocalJobQueueDescriptor descriptor,
        IEnumerable<ILocalJobHandler> handlers,
        RunningJobCancellationRegistry cancellationRegistry,
        IPerformanceSettingsService performanceSettings,
        InProcessBackendDescriptor backendDescriptor,
        OnlyRag.Core.IHardwareMonitorService? hardwareMonitor = null)
    {
        this.queue = queue;
        this.descriptor = descriptor;
        this.handlers = handlers.ToDictionary(handler => handler.Type, StringComparer.OrdinalIgnoreCase);
        this.cancellationRegistry = cancellationRegistry;
        this.performanceSettings = performanceSettings;
        this.backendDescriptor = backendDescriptor;
        this.hardwareMonitor = hardwareMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!descriptor.Persistent)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int configuredParallelJobs = Math.Max(1, (await performanceSettings.GetAsync(stoppingToken)).MaxParallelJobs);
            int workerCount = configuredParallelJobs;
            int delayMs = 50;

            if (hardwareMonitor != null)
            {
                (workerCount, delayMs) = await hardwareMonitor.GetThrottledWorkerParametersAsync(configuredParallelJobs, stoppingToken);
            }

            Task<bool>[] workers = Enumerable.Range(0, workerCount)
                .Select(_ => ProcessOneAsync(stoppingToken))
                .ToArray();
            bool didWork = (await Task.WhenAll(workers)).Any(result => result);

            if (!didWork)
            {
                // A retry can become eligible without a new enqueue signal.
                // Wake periodically so next_attempt_at_utc is observed.
                await queue.EnqueueSignal.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken);
            }
            else
            {
                await Task.Delay(delayMs, stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessOneAsync(CancellationToken stoppingToken)
    {
        if (handlers.Count == 0)
        {
            return false;
        }

        LocalJob? job = await queue.TryLeaseNextAsync(stoppingToken);
        if (job is null)
        {
            return false;
        }

        if (!handlers.TryGetValue(job.Type, out ILocalJobHandler? handler))
        {
            string noHandlerError = $"No handler registered for job type '{job.Type}'.";
            BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: {noHandlerError}");
            await queue.FailAsync(job.Id, noHandlerError, retryable: false, stoppingToken);
            return true;
        }

        BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: started.");

        CancellationTokenSource jobCancellation = cancellationRegistry.Register(job.Id, stoppingToken);
        try
        {
            LocalJob? latestBeforeExecute = await queue.GetAsync(job.Id, stoppingToken);
            if (latestBeforeExecute?.Status is not JobStatus.Running)
            {
                if (latestBeforeExecute?.Status is JobStatus.Pausing)
                {
                    await queue.CompletePauseAsync(job.Id, stoppingToken);
                    BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: paused.");
                }

                return true;
            }

            await handler.ExecuteAsync(job, queue, jobCancellation.Token);
            LocalJob? latest = await queue.GetAsync(job.Id, stoppingToken);
            if (latest?.Status is JobStatus.Running)
            {
                await queue.CompleteAsync(job.Id, stoppingToken);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: completed.");
            }
            else if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, stoppingToken);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: paused.");
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            LocalJob? latest = await queue.GetAsync(job.Id, CancellationToken.None);
            if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: paused.");
            }
            else if (latest?.Status is not (JobStatus.Cancelled or JobStatus.Paused))
            {
                await queue.CancelAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: cancelled.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Latest checkpoint is already in DB thanks to periodic handler saves.
            // Do not overwrite with initial job values — leave DB unchanged.
            BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: interrupted by shutdown, will resume on next startup.");
        }
        catch (Exception ex)
        {
            LocalJob? latest = await queue.GetAsync(job.Id, CancellationToken.None);
            if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: paused.");
            }
            else
            {
                string message = UserFacingErrorText.FromExternalDetail(
                    ex.Message,
                    "Local job failed to complete. Technical details available in local logs.");
                await queue.FailAsync(job.Id, message, retryable: true, CancellationToken.None);
                BackendLog.WriteException(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: failed.", ex);
            }
        }
        finally
        {
            cancellationRegistry.Unregister(job.Id);
        }

        return true;
    }
}
