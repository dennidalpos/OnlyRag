using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class OllamaModelPullJobHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_StoresStreamingProgress()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            OllamaModelPullJobHandler.JobType,
            JsonSerializer.Serialize(new OllamaModelPullJobPayload("gemma3:4b"))));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;
        OllamaModelPullJobHandler handler = new(new StreamingPullClient());

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal(100, stored.ProgressPercent);
        Assert.Contains("gemma3:4b", stored.CurrentStep, StringComparison.Ordinal);
        Assert.Contains("\"Digest\":\"sha256:layer-a\"", stored.CheckpointJson, StringComparison.Ordinal);
    }

    private sealed class StreamingPullClient : IOllamaClient
    {
        public async Task PullModelAsync(
            string modelName,
            Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
            CancellationToken cancellationToken = default)
        {
            await onProgress(new OllamaModelPullProgress("downloading", 100, 50, 50, "sha256:layer-a", "layer-a"), cancellationToken);
            await onProgress(new OllamaModelPullProgress("success", 100, 100, 100, "sha256:layer-a", "layer-a"), cancellationToken);
        }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OllamaModelSummary>>([]);

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            int? numCtx = null,
            object? format = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OllamaModelDetails(modelName, null));

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
            string modelName,
            IReadOnlyList<string> inputs,
            int? numCtx = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.ModelPullJob.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteSchemaInitializer initializer = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, initializer);
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
