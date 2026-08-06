using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class BatchIngestionQueueService : IBatchIngestionQueueService
{
    private const int MaxRetries = 3;
    private readonly ConcurrentDictionary<string, BatchIngestionJob> _jobs = new();
    private readonly ConcurrentDictionary<string, List<Channel<BatchIngestionProgressEvent>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    public Task<BatchIngestionJob> EnqueueBatchAsync(IReadOnlyList<string> filePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        string batchId = $"batch_{Guid.NewGuid():N}";

        var items = filePaths.Select(path => new BatchFileItem(
            FileId: $"file_{Guid.NewGuid():N}",
            FilePath: path,
            SizeBytes: File.Exists(path) ? new FileInfo(path).Length : 0,
            Status: BatchJobStatus.Queued
        )).ToList();

        var job = new BatchIngestionJob(
            batchId,
            items,
            BatchJobStatus.Queued,
            CompletedCount: 0,
            TotalCount: items.Count,
            CreatedAtUtc: DateTimeOffset.UtcNow);

        _jobs[batchId] = job;
        _subscribers[batchId] = new List<Channel<BatchIngestionProgressEvent>>();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokens[batchId] = cts;

        _ = Task.Run(() => ProcessBatchAsync(batchId, cts.Token), cts.Token);

        return Task.FromResult(job);
    }

    public Task<BatchIngestionJob?> GetBatchStatusAsync(string batchId, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(batchId, out var job);
        return Task.FromResult(job);
    }

    public Task CancelBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        if (_cancellationTokens.TryGetValue(batchId, out var cts))
        {
            cts.Cancel();
        }

        if (_jobs.TryGetValue(batchId, out var job))
        {
            _jobs[batchId] = job with { Status = BatchJobStatus.Cancelled, FinishedAtUtc = DateTimeOffset.UtcNow };
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<BatchIngestionProgressEvent> SubscribeProgressAsync(
        string batchId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<BatchIngestionProgressEvent>();

        if (_subscribers.TryGetValue(batchId, out var list))
        {
            lock (list)
            {
                list.Add(channel);
            }
        }

        if (_jobs.TryGetValue(batchId, out var existingJob))
        {
            foreach (var file in existingJob.Files)
            {
                if (file.Status != BatchJobStatus.Queued)
                {
                    channel.Writer.TryWrite(new BatchIngestionProgressEvent(
                        batchId, file.FileId, file.FilePath, existingJob.CompletedCount, existingJob.TotalCount, file.Status, file.ErrorMessage, file.RetryAttempt));
                }
            }

            if (existingJob.Status == BatchJobStatus.Completed || existingJob.Status == BatchJobStatus.Failed || existingJob.Status == BatchJobStatus.Cancelled)
            {
                channel.Writer.TryComplete();
            }
        }

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (channel.Reader.TryRead(out var evt))
                {
                    yield return evt;
                }
            }
        }
        finally
        {
            if (_subscribers.TryGetValue(batchId, out var subList))
            {
                lock (subList)
                {
                    subList.Remove(channel);
                }
            }
        }
    }

    private async Task ProcessBatchAsync(string batchId, CancellationToken ct)
    {
        if (!_jobs.TryGetValue(batchId, out var job)) return;

        _jobs[batchId] = job with { Status = BatchJobStatus.Processing };
        var fileList = job.Files.ToList();
        int completedCount = 0;

        for (int i = 0; i < fileList.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                _jobs[batchId] = _jobs[batchId] with { Status = BatchJobStatus.Cancelled, FinishedAtUtc = DateTimeOffset.UtcNow };
                return;
            }

            var item = fileList[i];
            fileList[i] = item with { Status = BatchJobStatus.Processing };
            PublishEvent(batchId, fileList[i].FileId, fileList[i].FilePath, completedCount, fileList.Count, BatchJobStatus.Processing);

            bool success = false;
            string? lastError = null;

            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    await ProcessSingleFileWithRetryAsync(item.FilePath, ct).ConfigureAwait(false);
                    success = true;
                    fileList[i] = fileList[i] with { Status = BatchJobStatus.Completed, RetryAttempt = attempt };
                    completedCount++;
                    PublishEvent(batchId, fileList[i].FileId, fileList[i].FilePath, completedCount, fileList.Count, BatchJobStatus.Completed, RetryAttempt: attempt);
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt < MaxRetries)
                    {
                        int delayMs = (int)Math.Pow(2, attempt - 1) * 50;
                        await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    }
                }
            }

            if (!success)
            {
                fileList[i] = fileList[i] with { Status = BatchJobStatus.Failed, ErrorMessage = lastError, RetryAttempt = MaxRetries };
                PublishEvent(batchId, fileList[i].FileId, fileList[i].FilePath, completedCount, fileList.Count, BatchJobStatus.Failed, ErrorMessage: lastError, RetryAttempt: MaxRetries);
            }

            _jobs[batchId] = _jobs[batchId] with { Files = fileList.ToList(), CompletedCount = completedCount };
        }

        _jobs[batchId] = _jobs[batchId] with
        {
            Status = fileList.Any(f => f.Status == BatchJobStatus.Failed) ? BatchJobStatus.Failed : BatchJobStatus.Completed,
            FinishedAtUtc = DateTimeOffset.UtcNow
        };

        CompleteSubscribers(batchId);
    }

    private void CompleteSubscribers(string batchId)
    {
        if (_subscribers.TryGetValue(batchId, out var list))
        {
            lock (list)
            {
                foreach (var channel in list)
                {
                    channel.Writer.TryComplete();
                }
            }
        }
    }

    private static async Task ProcessSingleFileWithRetryAsync(string filePath, CancellationToken ct)
    {
        await Task.Yield();
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found for batch ingestion: {filePath}");
        }
    }

    private void PublishEvent(
        string batchId,
        string fileId,
        string filePath,
        int completedCount,
        int totalCount,
        BatchJobStatus status,
        string? ErrorMessage = null,
        int RetryAttempt = 0)
    {
        var evt = new BatchIngestionProgressEvent(batchId, fileId, filePath, completedCount, totalCount, status, ErrorMessage, RetryAttempt);
        if (_subscribers.TryGetValue(batchId, out var list))
        {
            lock (list)
            {
                foreach (var channel in list)
                {
                    channel.Writer.TryWrite(evt);
                }
            }
        }
    }
}
