using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SqliteLocalJobQueueTests
{
    [Fact]
    public async Task CreateAsync_PersistsPendingJob()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue queue = await tempStorage.CreateInitializedQueueAsync();

        LocalJob job = await queue.CreateAsync(new CreateLocalJobRequest("ocr", "{\"documentId\":42}", Priority: 5));

        LocalJob? stored = await queue.GetAsync(job.Id);
        Assert.NotNull(stored);
        Assert.Equal("ocr", stored.Type);
        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(5, stored.Priority);
        Assert.Equal("{\"documentId\":42}", stored.PayloadJson);
        Assert.Equal("{}", stored.CheckpointJson);
    }

    [Fact]
    public async Task PauseAndResume_ApplyValidStatusTransitions()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue queue = await tempStorage.CreateInitializedQueueAsync();
        LocalJob job = await queue.CreateAsync(new CreateLocalJobRequest("embedding", "{}"));

        LocalJob? paused = await queue.PauseAsync(job.Id);
        LocalJob? resumed = await queue.ResumeAsync(job.Id);

        Assert.NotNull(paused);
        Assert.Equal(JobStatus.Paused, paused.Status);
        Assert.NotNull(resumed);
        Assert.Equal(JobStatus.Pending, resumed.Status);
    }

    [Fact]
    public async Task PauseAsync_RunningJobUsesPausingUntilWorkerFinalizesPause()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue queue = await tempStorage.CreateInitializedQueueAsync();
        LocalJob job = await queue.CreateAsync(new CreateLocalJobRequest("embedding", "{}"));
        LocalJob? leased = await queue.TryLeaseNextAsync();

        LocalJob? pausing = await queue.PauseAsync(job.Id);
        LocalJob? immediateResume = await queue.ResumeAsync(job.Id);
        LocalJob? paused = await queue.CompletePauseAsync(job.Id);
        LocalJob? resumed = await queue.ResumeAsync(job.Id);

        Assert.NotNull(leased);
        Assert.Equal(JobStatus.Running, leased.Status);
        Assert.NotNull(pausing);
        Assert.Equal(JobStatus.Pausing, pausing.Status);
        Assert.NotNull(immediateResume);
        Assert.Equal(JobStatus.Pausing, immediateResume.Status);
        Assert.NotNull(paused);
        Assert.Equal(JobStatus.Paused, paused.Status);
        Assert.NotNull(resumed);
        Assert.Equal(JobStatus.Pending, resumed.Status);
    }

    [Fact]
    public async Task SaveCheckpointAsync_PersistsProgressAndCheckpoint()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue queue = await tempStorage.CreateInitializedQueueAsync();
        LocalJob job = await queue.CreateAsync(new CreateLocalJobRequest("translation", "{}"));

        LocalJob? checkpointed = await queue.SaveCheckpointAsync(
            job.Id,
            new LocalJobCheckpoint(37, "Pagina 12", "{\"page\":12}"));

        Assert.NotNull(checkpointed);
        Assert.Equal(37, checkpointed.ProgressPercent);
        Assert.Equal("Pagina 12", checkpointed.CurrentStep);
        Assert.Equal("{\"page\":12}", checkpointed.CheckpointJson);
    }

    [Fact]
    public async Task RecoverInterruptedJobsAsync_ReturnsRunningJobsToPendingAfterRestart()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue firstQueue = await tempStorage.CreateInitializedQueueAsync();
        LocalJob job = await firstQueue.CreateAsync(new CreateLocalJobRequest("ingestion", "{}"));

        LocalJob? leased = await firstQueue.TryLeaseNextAsync();
        Assert.NotNull(leased);
        Assert.Equal(JobStatus.Running, leased.Status);

        SqliteLocalJobQueue restartedQueue = tempStorage.CreateQueue();
        int recovered = await restartedQueue.RecoverInterruptedJobsAsync();
        LocalJob? recoveredJob = await restartedQueue.GetAsync(job.Id);

        Assert.Equal(1, recovered);
        Assert.NotNull(recoveredJob);
        Assert.Equal(JobStatus.Pending, recoveredJob.Status);
    }

    [Fact]
    public async Task RecoverInterruptedJobsAsync_ReturnsPausingJobsToPausedAfterRestart()
    {
        using TempStorage tempStorage = TempStorage.Create();
        SqliteLocalJobQueue firstQueue = await tempStorage.CreateInitializedQueueAsync();
        LocalJob job = await firstQueue.CreateAsync(new CreateLocalJobRequest("ingestion", "{}"));
        LocalJob? leased = await firstQueue.TryLeaseNextAsync();
        Assert.NotNull(leased);
        await firstQueue.PauseAsync(job.Id);

        SqliteLocalJobQueue restartedQueue = tempStorage.CreateQueue();
        int recovered = await restartedQueue.RecoverInterruptedJobsAsync();
        LocalJob? recoveredJob = await restartedQueue.GetAsync(job.Id);

        Assert.Equal(1, recovered);
        Assert.NotNull(recoveredJob);
        Assert.Equal(JobStatus.Paused, recoveredJob.Status);
    }

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public static TempStorage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.JobQueue.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task<SqliteLocalJobQueue> CreateInitializedQueueAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, migrator);
            await storage.InitializeAsync();
            return CreateQueue();
        }

        public SqliteLocalJobQueue CreateQueue()
        {
            return new SqliteLocalJobQueue(CreateConnectionFactory(), LocalJobQueueDescriptor.Default);
        }

        private LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
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
