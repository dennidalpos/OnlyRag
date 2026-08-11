using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LocalJobQueueCompatibilityTests
{
    [Fact]
    public async Task QueueLifecycle_PreservesContractStatesAndCheckpoint()
    {
        using TestStorage storage = await TestStorage.CreateAsync();
        SqliteLocalJobQueue queue = storage.CreateQueue();

        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("test", """{"value":1}""", Priority: 3, MaxRetries: 1));
        Assert.Equal(JobStatus.Pending, created.Status);

        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        Assert.Equal(JobStatus.Running, leased.Status);

        LocalJob checkpointed = (await queue.SaveCheckpointAsync(
            leased.Id,
            new LocalJobCheckpoint(45, "processing", """{"offset":4}""")))!;
        Assert.Equal(45, checkpointed.ProgressPercent);
        Assert.Equal("processing", checkpointed.CurrentStep);
        Assert.Equal("""{"offset":4}""", checkpointed.CheckpointJson);

        LocalJob pausing = (await queue.PauseAsync(leased.Id))!;
        Assert.Equal(JobStatus.Pausing, pausing.Status);
        LocalJob paused = (await queue.CompletePauseAsync(leased.Id))!;
        Assert.Equal(JobStatus.Paused, paused.Status);
        LocalJob resumed = (await queue.ResumeAsync(leased.Id))!;
        Assert.Equal(JobStatus.Pending, resumed.Status);

        LocalJob leasedAgain = (await queue.TryLeaseNextAsync())!;
        LocalJob completed = (await queue.CompleteAsync(leasedAgain.Id))!;
        Assert.Equal(JobStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task QueueFailure_SchedulesRetryWithoutChangingPayload()
    {
        using TestStorage storage = await TestStorage.CreateAsync();
        SqliteLocalJobQueue queue = storage.CreateQueue();

        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("retry", """{"keep":true}""", MaxRetries: 1));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        LocalJob retried = (await queue.FailAsync(leased.Id, "temporary", retryable: true))!;

        Assert.Equal(JobStatus.Pending, retried.Status);
        Assert.Equal(1, retried.RetryCount);
        Assert.Equal("""{"keep":true}""", retried.PayloadJson);
        Assert.NotNull(retried.NextAttemptAtUtc);
    }

    [Fact]
    public async Task Recovery_ReturnsInterruptedRunningJobToPending()
    {
        using TestStorage storage = await TestStorage.CreateAsync();
        SqliteLocalJobQueue queue = storage.CreateQueue();

        await queue.CreateAsync(new CreateLocalJobRequest("recovery", "{}"));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        int recovered = await queue.RecoverInterruptedJobsAsync();
        LocalJob restored = (await queue.GetAsync(leased.Id))!;

        Assert.Equal(1, recovered);
        Assert.Equal(JobStatus.Pending, restored.Status);
    }

    private sealed class TestStorage : IDisposable
    {
        private TestStorage(string root, LocalSqliteStoreDescriptor descriptor, LocalSqliteConnectionFactory connectionFactory)
        {
            Root = root;
            Descriptor = descriptor;
            ConnectionFactory = connectionFactory;
        }

        private string Root { get; }
        private LocalSqliteStoreDescriptor Descriptor { get; }
        private LocalSqliteConnectionFactory ConnectionFactory { get; }

        public static async Task<TestStorage> CreateAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));
            AppStoragePaths paths = AppStoragePaths.FromRoot(root);
            LocalSqliteStoreDescriptor descriptor = new(paths);
            LocalSqliteConnectionFactory connectionFactory = new(descriptor);
            LocalSqliteStorageService storage = new(descriptor, new LocalSqliteSchemaInitializer(descriptor, connectionFactory));
            await storage.InitializeAsync();
            return new TestStorage(root, descriptor, connectionFactory);
        }

        public SqliteLocalJobQueue CreateQueue()
        {
            return new SqliteLocalJobQueue(
                ConnectionFactory,
                LocalJobQueueDescriptor.Default);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
