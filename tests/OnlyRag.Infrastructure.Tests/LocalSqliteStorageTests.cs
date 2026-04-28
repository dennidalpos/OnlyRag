using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LocalSqliteStorageTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndRequiredDirectories()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.True(File.Exists(tempStorage.Paths.DatabasePath));
        Assert.Equal(LocalSqliteMigrator.TargetSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal(LocalSqliteMigrator.TargetSchemaVersion, status.TargetSchemaVersion);
        Assert.Equal("Current", status.MigrationStatus);
        Assert.Equal(status.Fts5Available, await tempStorage.TableExistsAsync("chunks_fts"));
        Assert.True(status.Fts5Available || status.TechnicalNote is not null);

        foreach (string directory in tempStorage.Paths.EnumerateRequiredDirectories())
        {
            Assert.True(Directory.Exists(directory), directory);
        }
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        await storage.InitializeAsync();
        await storage.InitializeAsync();

        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;";
        command.Parameters.AddWithValue("$version", LocalSqliteMigrator.TargetSchemaVersion);
        object? count = await command.ExecuteScalarAsync();

        Assert.Equal(1L, count);
    }

    [Fact]
    public async Task InitializeAsync_CreatesCurrentFreshSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteMigrator.TargetSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal(1L, await CountRowsAsync(tempStorage, "schema_migrations", "version = $value", LocalSqliteMigrator.TargetSchemaVersion));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "file_extension"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "current_job_id"));
        Assert.True(await tempStorage.ColumnExistsAsync("chunks", "content_hash"));
        Assert.True(await tempStorage.ColumnExistsAsync("embeddings", "content_hash"));
        Assert.True(await tempStorage.ColumnExistsAsync("jobs", "checkpoint_json"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "machine_translated_text"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "layout_metadata_json"));
    }

    [Fact]
    public async Task InitializeAsync_RejectsPreexistingUnversionedSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await using (SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync())
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE legacy_data (id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.InitializeAsync());

        Assert.Contains("non esegue migrazioni dati", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsRepository_UpsertsAndReadsSetting()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();

        SqliteSettingsRepository repository = new(tempStorage.CreateConnectionFactory());

        await repository.UpsertAsync("ollama.baseUrl", "http://127.0.0.1:11434");
        await repository.UpsertAsync("ollama.baseUrl", "http://localhost:11434");

        string? value = await repository.GetValueAsync("ollama.baseUrl");

        Assert.Equal("http://localhost:11434", value);
    }

    [Fact]
    public async Task EmbeddingRepository_SavesEmbeddingAndTracksModelSpecificStatus()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();
        SqliteDocumentRepository documents = new(tempStorage.CreateConnectionFactory());
        SqliteEmbeddingRepository embeddings = new(tempStorage.CreateConnectionFactory());

        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-1",
            "sample.txt",
            Path.Combine(tempStorage.Root, "sample.txt"),
            "sha",
            "text/plain",
            ".txt",
            12,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "alpha beta"),
            [new IngestedDocumentChunk(1, 1, 0, "alpha beta", 2, "hash-a")],
            pageCount: 1);

        IReadOnlyList<DocumentChunkForEmbedding> firstModelChunks =
            await embeddings.ListChunksNeedingEmbeddingAsync(document.Id, "embed-a", afterChunkIndex: 0, take: 4);
        await embeddings.UpsertEmbeddingAsync(firstModelChunks[0].Id, "embed-a", "hash-a", [1f, 0f, 0f]);

        DocumentEmbeddingStatusSnapshot firstModelStatus =
            await embeddings.GetDocumentEmbeddingStatusAsync(document.Id, "embed-a");
        DocumentEmbeddingStatusSnapshot secondModelStatus =
            await embeddings.GetDocumentEmbeddingStatusAsync(document.Id, "embed-b");
        IReadOnlyList<DocumentChunkForEmbedding> secondModelChunks =
            await embeddings.ListChunksNeedingEmbeddingAsync(document.Id, "embed-b", afterChunkIndex: 0, take: 4);

        Assert.Equal(1, firstModelStatus.EmbeddedChunkCount);
        Assert.Equal(1, firstModelStatus.ChunkCount);
        Assert.Equal(0, secondModelStatus.EmbeddedChunkCount);
        Assert.Single(secondModelChunks);
    }

    [Fact]
    public async Task SqliteVecVectorSearchService_RanksWithSqliteVecExtension()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();
        LocalSqliteConnectionFactory connectionFactory = tempStorage.CreateConnectionFactory();
        SqliteDocumentRepository documents = new(connectionFactory);
        SqliteEmbeddingRepository embeddings = new(connectionFactory);
        SqliteVecVectorSearchService vectorSearch = new(connectionFactory);

        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-vec",
            "vectors.txt",
            Path.Combine(tempStorage.Root, "vectors.txt"),
            "sha-vec",
            "text/plain",
            ".txt",
            12,
            DocumentStatus.Indexed,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "alpha beta"),
            [
                new IngestedDocumentChunk(1, 1, 0, "alpha", 1, "hash-alpha"),
                new IngestedDocumentChunk(1, 1, 1, "beta", 1, "hash-beta")
            ],
            pageCount: 1);

        IReadOnlyList<DocumentChunkForEmbedding> chunks =
            await embeddings.ListChunksNeedingEmbeddingAsync(document.Id, "embed-vec", afterChunkIndex: 0, take: 4);
        await embeddings.UpsertEmbeddingAsync(chunks.Single(chunk => chunk.ChunkIndex == 0).Id, "embed-vec", "hash-alpha", [1f, 0f]);
        await embeddings.UpsertEmbeddingAsync(chunks.Single(chunk => chunk.ChunkIndex == 1).Id, "embed-vec", "hash-beta", [0f, 1f]);

        IReadOnlyList<VectorSearchResult> results = await vectorSearch.SearchAsync(
            "embed-vec",
            [0.9f, 0.1f],
            [document.Id],
            limit: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(0, results[0].ChunkIndex);
        Assert.True(results[0].Score > results[1].Score);
        Assert.Contains("sqlite-vec", vectorSearch.BackendName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(int.MaxValue, vectorSearch.MaxSearchableVectors);
    }

    [Fact]
    public async Task DocumentRepository_ClearIngestionAsync_RemovesFtsBackedChunks()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();
        SqliteDocumentRepository documents = new(tempStorage.CreateConnectionFactory());

        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-fts",
            "sample.txt",
            Path.Combine(tempStorage.Root, "sample.txt"),
            "sha-fts",
            "text/plain",
            ".txt",
            12,
            DocumentStatus.Imported,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "alpha beta"),
            [new IngestedDocumentChunk(1, 1, 0, "alpha beta", 2, "hash-fts")],
            pageCount: 1);

        await documents.ClearIngestionAsync(document.Id);

        Assert.Equal(0, await CountRowsAsync(tempStorage, "chunks", "document_id = $documentId", document.Id));
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));
            return new TempStorage(root);
        }

        public LocalSqliteStorageService CreateStorageService()
        {
            LocalSqliteConnectionFactory connectionFactory = CreateConnectionFactory();
            LocalSqliteMigrator migrator = new(Descriptor, connectionFactory);
            return new LocalSqliteStorageService(Descriptor, migrator);
        }

        public LocalSqliteConnectionFactory CreateConnectionFactory()
        {
            return new LocalSqliteConnectionFactory(Descriptor);
        }

        public async Task<bool> TableExistsAsync(string tableName)
        {
            await using SqliteConnection connection = await CreateConnectionFactory().OpenConnectionAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", tableName);
            object? value = await command.ExecuteScalarAsync();
            return value is not null;
        }

        public async Task<bool> ColumnExistsAsync(string tableName, string columnName)
        {
            await using SqliteConnection connection = await CreateConnectionFactory().OpenConnectionAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static async Task<long> CountRowsAsync(
        TempStorage tempStorage,
        string tableName,
        string whereClause,
        long value)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {whereClause};";
        command.Parameters.AddWithValue("$documentId", value);
        command.Parameters.AddWithValue("$value", value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
