using System.Text;
using System.Text.Json;
using OnlyRag.Api;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class DocumentIngestionJobHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_WhenIngestionProducesChunks_AutoQueuesEmbeddingJob()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();

        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        StubDocumentLibraryService libraryService = new(documentRepository, queue);

        ImportedDocument document = await CreateQueuedDocumentAsync(documentRepository, tempStorage.Root);

        FakeIngestionService ingestionService = new(pageCount: 3, chunkCount: 12);
        StubOllamaSettingsService settingsService = new(new OllamaSettings(
            OllamaEndpointOptions.DefaultBaseUrl,
            null,
            "embed-model",
            null,
            60,
            1));

        DocumentIngestionJobHandler handler = new(
            libraryService,
            ingestionService,
            tempStorage.CreateDescriptor(),
            settingsService);

        LocalJob ingestionJob = await queue.CreateAsync(new CreateLocalJobRequest(
            LocalDocumentLibraryService.DocumentIngestionJobType,
            JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty)),
            MaxRetries: 0));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();
        LocalJob? embeddingJob = jobs.FirstOrDefault(j => j.Type == DocumentEmbeddingJobHandler.DocumentEmbeddingJobType);
        Assert.NotNull(embeddingJob);

        DocumentEmbeddingJobPayload? payload = JsonSerializer.Deserialize<DocumentEmbeddingJobPayload>(embeddingJob.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(document.Id, payload.DocumentId);
        Assert.Equal("embed-model", payload.Model);
    }

    [Fact]
    public async Task ExecuteAsync_WhenIngestionProducesZeroChunks_DoesNotQueueEmbeddingJob()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();

        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        StubDocumentLibraryService libraryService = new(documentRepository, queue);

        ImportedDocument document = await CreateQueuedDocumentAsync(documentRepository, tempStorage.Root);

        FakeIngestionService ingestionService = new(pageCount: 1, chunkCount: 0);
        StubOllamaSettingsService settingsService = new(new OllamaSettings(
            OllamaEndpointOptions.DefaultBaseUrl,
            null,
            "embed-model",
            null,
            60,
            1));

        DocumentIngestionJobHandler handler = new(
            libraryService,
            ingestionService,
            tempStorage.CreateDescriptor(),
            settingsService);

        await queue.CreateAsync(new CreateLocalJobRequest(
            LocalDocumentLibraryService.DocumentIngestionJobType,
            JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty)),
            MaxRetries: 0));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();
        Assert.DoesNotContain(jobs, j => j.Type == DocumentEmbeddingJobHandler.DocumentEmbeddingJobType);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEmbeddingModelNotConfigured_DoesNotQueueEmbeddingJob()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();

        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        StubDocumentLibraryService libraryService = new(documentRepository, queue);

        ImportedDocument document = await CreateQueuedDocumentAsync(documentRepository, tempStorage.Root);

        FakeIngestionService ingestionService = new(pageCount: 2, chunkCount: 8);
        StubOllamaSettingsService settingsService = new(new OllamaSettings(
            OllamaEndpointOptions.DefaultBaseUrl,
            null,
            DefaultEmbeddingModel: null,
            null,
            60,
            1));

        DocumentIngestionJobHandler handler = new(
            libraryService,
            ingestionService,
            tempStorage.CreateDescriptor(),
            settingsService);

        await queue.CreateAsync(new CreateLocalJobRequest(
            LocalDocumentLibraryService.DocumentIngestionJobType,
            JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty)),
            MaxRetries: 0));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        IReadOnlyList<LocalJob> jobs = await queue.ListAsync();
        Assert.DoesNotContain(jobs, j => j.Type == DocumentEmbeddingJobHandler.DocumentEmbeddingJobType);
    }

    [Fact]
    public async Task ExecuteAsync_SetsDocumentStatusToIndexedOnSuccess()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();

        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        StubDocumentLibraryService libraryService = new(documentRepository, queue);

        ImportedDocument document = await CreateQueuedDocumentAsync(documentRepository, tempStorage.Root);

        DocumentIngestionJobHandler handler = new(
            libraryService,
            new FakeIngestionService(pageCount: 2, chunkCount: 6),
            tempStorage.CreateDescriptor(),
            new StubOllamaSettingsService(new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                null,
                null,
                60,
                1)));

        await queue.CreateAsync(new CreateLocalJobRequest(
            LocalDocumentLibraryService.DocumentIngestionJobType,
            JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty)),
            MaxRetries: 0));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        ImportedDocument? updated = await documentRepository.GetAsync(document.Id);
        Assert.NotNull(updated);
        Assert.Equal(DocumentStatus.Indexed, updated.Status);
    }

    [Fact]
    public async Task ExecuteAsync_PassesOcrLanguageFromPayloadToIngestion()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await tempStorage.InitializeAsync();

        SqliteDocumentRepository documentRepository = new(tempStorage.CreateConnectionFactory());
        SqliteLocalJobQueue queue = tempStorage.CreateQueue();
        StubDocumentLibraryService libraryService = new(documentRepository, queue);
        ImportedDocument document = await CreateQueuedDocumentAsync(documentRepository, tempStorage.Root);
        FakeIngestionService ingestionService = new(pageCount: 1, chunkCount: 1);
        DocumentIngestionJobHandler handler = new(
            libraryService,
            ingestionService,
            tempStorage.CreateDescriptor(),
            new StubOllamaSettingsService(new OllamaSettings(
                OllamaEndpointOptions.DefaultBaseUrl,
                null,
                null,
                null,
                60,
                1)));

        await queue.CreateAsync(new CreateLocalJobRequest(
            LocalDocumentLibraryService.DocumentIngestionJobType,
            JsonSerializer.Serialize(new DocumentIngestionJobPayload(
                document.Id,
                document.DocumentUid,
                document.OriginalFileName,
                document.Sha256 ?? string.Empty,
                OcrLanguage: "fr")),
            MaxRetries: 0));
        LocalJob leased = (await queue.TryLeaseNextAsync())!;

        await handler.ExecuteAsync(leased, queue, CancellationToken.None);

        Assert.Equal("fr", ingestionService.LastOcrLanguage);
    }

    private static async Task<ImportedDocument> CreateQueuedDocumentAsync(
        SqliteDocumentRepository repository,
        string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "originals"));
        string filePath = Path.Combine(root, "originals", "test-doc.txt");
        await File.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes("test content"));

        return await repository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString("N"),
            "test-doc.txt",
            filePath,
            "abc123",
            "text/plain",
            ".txt",
            12,
            DocumentStatus.Queued,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
    }

    // -----------------------------------------------------------------------
    // Fakes & stubs
    // -----------------------------------------------------------------------

    private sealed class FakeIngestionService : IDocumentIngestionService
    {
        private readonly int pageCount;
        private readonly int chunkCount;

        public FakeIngestionService(int pageCount, int chunkCount)
        {
            this.pageCount = pageCount;
            this.chunkCount = chunkCount;
        }

        public string? LastOcrLanguage { get; private set; }

        public Task<DocumentIngestionResult> IngestAsync(
            ImportedDocument document,
            DocumentIngestionCheckpoint? checkpoint,
            Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
            bool forceOcr = false,
            string? ocrLanguage = null,
            CancellationToken cancellationToken = default)
        {
            LastOcrLanguage = ocrLanguage;
            return Task.FromResult(new DocumentIngestionResult(pageCount, chunkCount));
        }
    }

    private sealed class StubDocumentLibraryService : IDocumentLibraryService
    {
        private readonly SqliteDocumentRepository documents;
        private readonly SqliteLocalJobQueue queue;

        public StubDocumentLibraryService(SqliteDocumentRepository documents, SqliteLocalJobQueue queue)
        {
            this.documents = documents;
            this.queue = queue;
        }

        public Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default) =>
            documents.ListAsync(cancellationToken);

        public Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default) =>
            documents.GetAsync(id, cancellationToken);

        public Task<DocumentImportResult> ImportAsync(
            Stream source,
            string fileName,
            bool forceOcr = false,
            string? ocrLanguage = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportedDocument?> QueueForIndexingAsync(
            long id,
            string? ocrLanguage = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ImportedDocument?> SetStatusAsync(
            long id,
            DocumentStatus status,
            string? currentJobId,
            string? lastError,
            CancellationToken cancellationToken = default) =>
            documents.UpdateStatusAsync(id, status, currentJobId, lastError, cancellationToken);

        public Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
            documents.DeleteAsync(id, cancellationToken);
    }

    private sealed class StubOllamaSettingsService : IOllamaSettingsService
    {
        private readonly OllamaSettings settings;

        public StubOllamaSettingsService(OllamaSettings settings) => this.settings = settings;

        public Task<OllamaSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task<OllamaSettings> UpdateAsync(OllamaSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearMissingDefaultModelAsync(string modelName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.IngestionJob.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public async Task InitializeAsync()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteSchemaInitializer initializer = new(Descriptor, connectionFactory);
            LocalSqliteStorageService storage = new(Descriptor, initializer);
            await storage.InitializeAsync();
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory() =>
            new(Descriptor);

        public SqliteLocalJobQueue CreateQueue() =>
            new(CreateConnectionFactory(), LocalJobQueueDescriptor.Default);

        public InProcessBackendDescriptor CreateDescriptor() =>
            new(Paths, Descriptor, LocalJobQueueDescriptor.Default, new OllamaEndpointOptions());

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
