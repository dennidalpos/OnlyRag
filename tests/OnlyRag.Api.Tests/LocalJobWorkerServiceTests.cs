using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class LocalJobWorkerServiceTests
{
    [Fact]
    public async Task ExecuteAsync_RespectsMaxParallelJobsSetting()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        await queue.CreateAsync(new CreateLocalJobRequest("slow", "{}"));
        await queue.CreateAsync(new CreateLocalJobRequest("slow", "{}"));
        SlowHandler handler = new();
        InProcessBackendDescriptor backendDescriptor = new(
            tempStorage.Paths,
            tempStorage.Descriptor,
            LocalJobQueueDescriptor.Default,
            new OllamaEndpointOptions());
        LocalJobWorkerService service = new(
            queue,
            LocalJobQueueDescriptor.Default,
            [handler],
            new RunningJobCancellationRegistry(),
            new StubPerformanceSettingsService(new PerformanceSettings(2, 1, 1, 1, 8, 60, false)),
            backendDescriptor);

        await service.StartAsync(CancellationToken.None);
        Task completed = await Task.WhenAny(handler.TwoRunning.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await service.StopAsync(CancellationToken.None);

        Assert.Same(handler.TwoRunning.Task, completed);
        Assert.True(handler.MaxObservedConcurrency >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledRunningJobStopsHandlerAndMarksCancelled()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("slow", "{}"));
        SlowHandler handler = new();
        RunningJobCancellationRegistry registry = new();
        InProcessBackendDescriptor backendDescriptor2 = new(
            tempStorage.Paths,
            tempStorage.Descriptor,
            LocalJobQueueDescriptor.Default,
            new OllamaEndpointOptions());
        LocalJobWorkerService service = new(
            queue,
            LocalJobQueueDescriptor.Default,
            [handler],
            registry,
            new StubPerformanceSettingsService(new PerformanceSettings(1, 1, 1, 1, 8, 60, false)),
            backendDescriptor2);

        await service.StartAsync(CancellationToken.None);
        Task started = await Task.WhenAny(handler.OneRunning.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(handler.OneRunning.Task, started);

        await queue.CancelAsync(created.Id);
        registry.Cancel(created.Id);
        Task cancelled = await Task.WhenAny(handler.Cancelled.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        await service.StopAsync(CancellationToken.None);
        LocalJob? stored = await queue.GetAsync(created.Id);

        Assert.Same(handler.Cancelled.Task, cancelled);
        Assert.NotNull(stored);
        Assert.Equal(JobStatus.Cancelled, stored.Status);
    }

    private sealed class SlowHandler : ILocalJobHandler
    {
        private int running;
        private int maxObservedConcurrency;

        public string Type => "slow";

        public TaskCompletionSource OneRunning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TwoRunning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxObservedConcurrency => maxObservedConcurrency;

        public async Task ExecuteAsync(LocalJob job, ILocalJobQueue queue, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref running);
            UpdateMax(current);
            OneRunning.TrySetResult();
            if (current >= 2)
            {
                TwoRunning.TrySetResult();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        }

        private void UpdateMax(int current)
        {
            while (true)
            {
                int previous = maxObservedConcurrency;
                if (current <= previous || Interlocked.CompareExchange(ref maxObservedConcurrency, current, previous) == previous)
                {
                    return;
                }
            }
        }
    }

    private sealed class StubPerformanceSettingsService : IPerformanceSettingsService
    {
        private readonly PerformanceSettings settings;

        public StubPerformanceSettingsService(PerformanceSettings settings)
        {
            this.settings = settings;
        }

        public Task<PerformanceSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settings);
        }

        public Task<PerformanceSettings> UpdateAsync(PerformanceSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Worker.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, migrator);
            await storage.InitializeAsync();
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
        }

        public SqliteLocalJobQueue CreateQueue()
        {
            return new SqliteLocalJobQueue(CreateConnectionFactory(), LocalJobQueueDescriptor.Default);
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
