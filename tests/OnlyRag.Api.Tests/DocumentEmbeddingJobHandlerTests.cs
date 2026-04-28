using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class DocumentEmbeddingJobHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_UsesPerformanceEmbeddingBatchSize()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteEmbeddingRepository embeddingRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        CapturingEmbeddingClient ollamaClient = new();
        DocumentEmbeddingJobHandler handler = new(
            new StubDocumentLibraryService(documentRepository),
            embeddingRepository,
            ollamaClient,
            new StubOllamaSettingsService(new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                "embed-a",
                null,
                60,
                1)),
            new StubPerformanceSettingsService(new PerformanceSettings(
                1,
                1,
                2,
                1,
                8,
                60,
                false)));
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentEmbeddingJobHandler.DocumentEmbeddingJobType,
            JsonSerializer.Serialize(new DocumentEmbeddingJobPayload(document.Id, "embed-a")),
            MaxRetries: 2));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        Assert.Equal(JobStatus.Running, stored.Status);
        Assert.Equal(new[] { 2 }, ollamaClient.BatchSizes);
    }

    [Fact]
    public async Task ExecuteAsync_CheckpointsAfterEachChunkBeforeRetryableFailure()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();
        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteEmbeddingRepository embeddingRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        ImportedDocument document = await CreateIndexedDocumentAsync(documentRepository, tempStorage.Root);
        StubDocumentLibraryService documents = new(documentRepository);
        FailingAfterFirstEmbeddingClient ollamaClient = new();
        DocumentEmbeddingJobHandler handler = new(
            documents,
            embeddingRepository,
            ollamaClient,
            new StubOllamaSettingsService(new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                "embed-a",
                null,
                60,
                1)),
            new StubPerformanceSettingsService(new PerformanceSettings(
                1,
                1,
                1,
                1,
                8,
                60,
                false)));
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest(
            DocumentEmbeddingJobHandler.DocumentEmbeddingJobType,
            JsonSerializer.Serialize(new DocumentEmbeddingJobPayload(document.Id, "embed-a")),
            MaxRetries: 2));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        LocalJob stored = (await queue.GetAsync(created.Id))!;
        DocumentEmbeddingStatusSnapshot status =
            await embeddingRepository.GetDocumentEmbeddingStatusAsync(document.Id, "embed-a");

        Assert.Equal(JobStatus.Pending, stored.Status);
        Assert.Equal(1, stored.RetryCount);
        Assert.Equal(50, stored.ProgressPercent);
        Assert.Contains("\"NextChunkIndex\":1", stored.CheckpointJson, StringComparison.Ordinal);
        Assert.Equal(1, status.EmbeddedChunkCount);
        Assert.Equal(2, status.ChunkCount);
        Assert.Equal(2, ollamaClient.CallCount);
    }

    private static async Task<ImportedDocument> CreateIndexedDocumentAsync(
        SqliteDocumentRepository documents,
        string root)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-1",
            "sample.txt",
            Path.Combine(root, "sample.txt"),
            "sha",
            "text/plain",
            ".txt",
            24,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            now,
            now));

        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "alpha beta gamma delta"),
            [
                new IngestedDocumentChunk(1, 1, 0, "alpha beta", 2, "hash-a"),
                new IngestedDocumentChunk(1, 1, 1, "gamma delta", 2, "hash-b")
            ],
            pageCount: 1);

        return document;
    }

    private sealed class StubDocumentLibraryService : IDocumentLibraryService
    {
        private readonly SqliteDocumentRepository documents;

        public StubDocumentLibraryService(SqliteDocumentRepository documents)
        {
            this.documents = documents;
        }

        public Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default)
        {
            return documents.ListAsync(cancellationToken);
        }

        public Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            return documents.GetAsync(id, cancellationToken);
        }

        public Task<DocumentImportResult> ImportAsync(
            Stream source,
            string fileName,
            bool forceOcr = false,
            string? ocrLanguage = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ImportedDocument?> QueueForIndexingAsync(
            long id,
            string? ocrLanguage = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<ImportedDocument?> SetStatusAsync(
            long id,
            DocumentStatus status,
            string? currentJobId,
            string? lastError,
            CancellationToken cancellationToken = default)
        {
            return documents.UpdateStatusAsync(id, status, currentJobId, lastError, cancellationToken);
        }

        public Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            return documents.DeleteAsync(id, cancellationToken);
        }
    }

    private sealed class StubOllamaSettingsService : IOllamaSettingsService
    {
        private readonly OllamaSettings settings;

        public StubOllamaSettingsService(OllamaSettings settings)
        {
            this.settings = settings;
        }

        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(settings);
        }

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

    private sealed class FailingAfterFirstEmbeddingClient : IOllamaClient
    {
        public int CallCount { get; private set; }

        public Task TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>([]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaModelDetails(modelName, null));
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
            string modelName,
            IReadOnlyList<string> inputs,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 2)
            {
                throw new OllamaApiException(OllamaErrorKind.Timeout, "timeout");
            }

            return Task.FromResult<IReadOnlyList<IReadOnlyList<float>>>([new[] { 1f, 0f, 0f }]);
        }
    }

    private sealed class CapturingEmbeddingClient : IOllamaClient
    {
        public List<int> BatchSizes { get; } = [];

        public Task TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OllamaModelSummary>>([]);
        }

        public Task PullModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<string> GenerateChatAsync(
            string modelName,
            IReadOnlyList<OllamaChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OllamaModelDetails(modelName, null));
        }

        public Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
            string modelName,
            IReadOnlyList<string> inputs,
            int? numCtx = null,
            CancellationToken cancellationToken = default)
        {
            BatchSizes.Add(inputs.Count);
            IReadOnlyList<IReadOnlyList<float>> vectors = inputs
                .Select(_ => (IReadOnlyList<float>)new[] { 1f, 0f, 0f })
                .ToArray();
            return Task.FromResult(vectors);
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.EmbeddingJob.Tests", Guid.NewGuid().ToString("N"));
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
