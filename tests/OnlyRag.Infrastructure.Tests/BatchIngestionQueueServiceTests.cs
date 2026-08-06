using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class BatchIngestionQueueServiceTests
{
    [Fact]
    public async Task EnqueueBatchAsync_ProcessesFiles_AndFiresProgressEvents()
    {
        var service = new BatchIngestionQueueService();
        string tempFile = Path.GetTempFileName();
        File.WriteAllText(tempFile, "Test ingestion batch payload");

        try
        {
            var job = await service.EnqueueBatchAsync(new[] { tempFile });
            Assert.NotNull(job);
            Assert.Equal(1, job.TotalCount);

            var events = new List<BatchIngestionProgressEvent>();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await foreach (var evt in service.SubscribeProgressAsync(job.BatchId, cts.Token))
            {
                events.Add(evt);
                if (evt.FileStatus == BatchJobStatus.Completed || evt.FileStatus == BatchJobStatus.Failed)
                {
                    break;
                }
            }

            var finalJob = await service.GetBatchStatusAsync(job.BatchId);
            Assert.NotNull(finalJob);
            Assert.Equal(BatchJobStatus.Completed, finalJob.Status);
            Assert.NotEmpty(events);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task EnqueueBatchAsync_RetriesNonExistentFile_AndFailsAfterMaxAttempts()
    {
        var service = new BatchIngestionQueueService();
        string missingFile = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.txt");

        var job = await service.EnqueueBatchAsync(new[] { missingFile });
        Assert.NotNull(job);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var evt in service.SubscribeProgressAsync(job.BatchId, cts.Token))
        {
            if (evt.FileStatus == BatchJobStatus.Failed)
            {
                Assert.Equal(3, evt.RetryAttempt);
                break;
            }
        }

        var finalJob = await service.GetBatchStatusAsync(job.BatchId);
        Assert.NotNull(finalJob);
        Assert.Equal(BatchJobStatus.Failed, finalJob.Status);
    }
}
