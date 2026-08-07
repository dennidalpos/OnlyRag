using System.Runtime.CompilerServices;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Storage.Security;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class StreamingDocumentIngestionPipelineTests
{
    [Fact]
    public async Task ProcessStreamAsync_StreamsPageBlocksThroughChannels_AndSavesChunksToSqlite()
    {
        using LocalSqliteStorageTests_TempStorage tempStorage = await LocalSqliteStorageTests_TempStorage.CreateInitializedAsync();
        var repository = tempStorage.CreateDocumentRepository();
        var chunker = new DocumentTextChunker();
        var pipeline = new StreamingDocumentIngestionPipeline(repository, chunker);

        string origPath = Path.Combine(tempStorage.Paths.DocumentOriginalsDirectory, "stream_doc.txt");
        Directory.CreateDirectory(tempStorage.Paths.DocumentOriginalsDirectory);
        await File.WriteAllTextAsync(origPath, "test content");

        var doc = await repository.CreateAsync(new CreateDocumentRecordRequest(
            Guid.NewGuid().ToString(),
            "stream_doc.txt",
            origPath,
            "hash_123",
            "text/plain",
            ".txt",
            1024,
            DocumentStatus.Imported,
            0,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow), CancellationToken.None);

        async IAsyncEnumerable<ParsedPageBlock> GenerateBlocks([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ParsedPageBlock(doc.Id, 1, "First page text content in streaming mode.");
            yield return new ParsedPageBlock(doc.Id, 2, "Second page text content with additional information.");
            yield return new ParsedPageBlock(doc.Id, 3, "Third page text content testing channel backpressure.");
        }

        var progressList = new List<DocumentIngestionProgress>();
        var result = await pipeline.ProcessStreamAsync(
            doc,
            GenerateBlocks(),
            DocumentIngestionOptions.Default,
            new StreamingIngestionOptions(PageBlockChannelCapacity: 2, ChunkBatchChannelCapacity: 2, EnableVectorStoreWriter: false),
            (progress, ct) =>
            {
                lock (progressList)
                {
                    progressList.Add(progress);
                }
                return Task.CompletedTask;
            },
            embeddingModel: null,
            cancellationToken: CancellationToken.None);

        Assert.Equal(3, result.PageCount);
        Assert.True(result.ChunkCount >= 3);

        var loadedDoc = await repository.GetAsync(doc.Id);
        Assert.NotNull(loadedDoc);
        Assert.Equal(3, loadedDoc.PageCount);
        Assert.Equal(result.ChunkCount, loadedDoc.ChunkCount);
        Assert.NotEmpty(progressList);
    }

    private sealed class LocalSqliteStorageTests_TempStorage : IDisposable
    {
        private LocalSqliteStorageTests_TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
        }

        public string Root { get; }
        public AppStoragePaths Paths { get; }
        public LocalSqliteStoreDescriptor Descriptor { get; }

        public static async Task<LocalSqliteStorageTests_TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));
            var storage = new LocalSqliteStorageTests_TempStorage(root);
            var connectionFactory = storage.CreateConnectionFactory();
            var initializer = new LocalSqliteSchemaInitializer(storage.Descriptor, connectionFactory);
            var storageService = new LocalSqliteStorageService(storage.Descriptor, initializer);
            await storageService.InitializeAsync();
            return storage;
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor, new HardcodedSqliteKeyProvider());
        }

        public SqliteDocumentRepository CreateDocumentRepository()
        {
            return new SqliteDocumentRepository(CreateConnectionFactory());
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class HardcodedSqliteKeyProvider : ISqliteKeyProvider
    {
        public string GetOrCreateDatabaseKey()
        {
            return "0000000000000000000000000000000000000000000000000000000000000000";
        }
    }
}
