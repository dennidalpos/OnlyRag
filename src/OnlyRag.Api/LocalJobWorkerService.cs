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

    public LocalJobWorkerService(
        ILocalJobQueue queue,
        LocalJobQueueDescriptor descriptor,
        IEnumerable<ILocalJobHandler> handlers,
        RunningJobCancellationRegistry cancellationRegistry,
        IPerformanceSettingsService performanceSettings,
        InProcessBackendDescriptor backendDescriptor)
    {
        this.queue = queue;
        this.descriptor = descriptor;
        this.handlers = handlers.ToDictionary(handler => handler.Type, StringComparer.OrdinalIgnoreCase);
        this.cancellationRegistry = cancellationRegistry;
        this.performanceSettings = performanceSettings;
        this.backendDescriptor = backendDescriptor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!descriptor.Persistent)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            int workerCount = Math.Max(1, (await performanceSettings.GetAsync(stoppingToken)).MaxParallelJobs);
            Task<bool>[] workers = Enumerable.Range(0, workerCount)
                .Select(_ => ProcessOneAsync(stoppingToken))
                .ToArray();
            bool didWork = (await Task.WhenAll(workers)).Any(result => result);

            if (!didWork)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
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
            string noHandlerError = $"Nessun handler registrato per il job '{job.Type}'.";
            BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: {noHandlerError}");
            await queue.FailAsync(job.Id, noHandlerError, retryable: false, stoppingToken);
            return true;
        }

        BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: avviato.");

        CancellationTokenSource jobCancellation = cancellationRegistry.Register(job.Id, stoppingToken);
        try
        {
            LocalJob? latestBeforeExecute = await queue.GetAsync(job.Id, stoppingToken);
            if (latestBeforeExecute?.Status is not JobStatus.Running)
            {
                if (latestBeforeExecute?.Status is JobStatus.Pausing)
                {
                    await queue.CompletePauseAsync(job.Id, stoppingToken);
                    BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: in pausa.");
                }

                return true;
            }

            await handler.ExecuteAsync(job, queue, jobCancellation.Token);
            LocalJob? latest = await queue.GetAsync(job.Id, stoppingToken);
            if (latest?.Status is JobStatus.Running)
            {
                await queue.CompleteAsync(job.Id, stoppingToken);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: completato.");
            }
            else if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, stoppingToken);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: in pausa.");
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            LocalJob? latest = await queue.GetAsync(job.Id, CancellationToken.None);
            if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: in pausa.");
            }
            else if (latest?.Status is not (JobStatus.Cancelled or JobStatus.Paused))
            {
                await queue.CancelAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: annullato.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Il checkpoint più recente è già in DB grazie ai salvataggi periodici dell'handler.
            // Non sovrascrivere con i valori iniziali del job — lasciare il DB invariato.
            BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: interrotto per shutdown, verrà ripreso al prossimo avvio.");
        }
        catch (Exception ex)
        {
            LocalJob? latest = await queue.GetAsync(job.Id, CancellationToken.None);
            if (latest?.Status is JobStatus.Pausing)
            {
                await queue.CompletePauseAsync(job.Id, CancellationToken.None);
                BackendLog.Write(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: in pausa.");
            }
            else
            {
                await queue.FailAsync(job.Id, ex.Message, retryable: true, CancellationToken.None);
                BackendLog.WriteException(backendDescriptor.StoragePaths, job.Id, $"Job {job.Type}: fallito.", ex);
            }
        }
        finally
        {
            cancellationRegistry.Unregister(job.Id);
        }

        return true;
    }
}
