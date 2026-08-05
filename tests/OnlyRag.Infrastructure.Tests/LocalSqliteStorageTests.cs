using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class LocalSqliteStorageTests
{
    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndRequiredDirectories()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.True(File.Exists(tempStorage.Paths.DatabasePath));
        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, status.TargetSchemaVersion);
        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal("Current", status.SchemaStatus);
        Assert.True(status.Fts5Available || status.TechnicalNote is not null);
        if (status.Fts5Available)
        {
            Assert.True(await tempStorage.TableExistsAsync("chunks_fts"));
        }

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

        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, await ReadUserVersionAsync(tempStorage));
        Assert.False(await tempStorage.TableExistsAsync("schema_migrations"));
    }

    [Fact]
    public async Task InitializeAsync_CreatesCurrentFreshSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, await ReadUserVersionAsync(tempStorage));
        Assert.False(await tempStorage.TableExistsAsync("schema_migrations"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "file_extension"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "current_job_id"));
        Assert.True(await tempStorage.ColumnExistsAsync("chunks", "content_hash"));
        Assert.True(await tempStorage.ColumnExistsAsync("chunk_vector_index_status", "qdrant_collection"));
        Assert.True(await tempStorage.ColumnExistsAsync("jobs", "checkpoint_json"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "machine_translated_text"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "layout_metadata_json"));
        Assert.True(await tempStorage.IndexExistsAsync("ux_documents_sha256_not_null"));
        Assert.True(await tempStorage.TableExistsAsync("archive_manifest_entries"));
        Assert.True(await tempStorage.IndexExistsAsync("idx_archive_manifest_path"));
    }

    [Fact]
    public async Task InitializeAsync_ResetsVersionedSchemaAndCreatesBackup()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateVersionedSchemaAsync(tempStorage);
        Directory.CreateDirectory(tempStorage.Paths.LogsDirectory);
        string staleLogPath = Path.Combine(tempStorage.Paths.LogsDirectory, "stale.log");
        await File.WriteAllTextAsync(staleLogPath, "old data");

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, await ReadUserVersionAsync(tempStorage));
        Assert.Equal("Current", status.SchemaStatus);
        Assert.Contains("resettato", status.TechnicalNote ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(staleLogPath));
        string backupRoot = Assert.Single(tempStorage.ListBackupRoots());
        Assert.True(File.Exists(Path.Combine(backupRoot, "logs", "stale.log")));
        Assert.True(await tempStorage.TableExistsAsync("documents"));
        Assert.True(await tempStorage.TableExistsAsync("document_pages"));
        Assert.True(await tempStorage.TableExistsAsync("chunk_vector_index_status"));
        Assert.False(await tempStorage.TableExistsAsync("schema_migrations"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "file_extension"));
        Assert.True(await tempStorage.ColumnExistsAsync("jobs", "checkpoint_json"));
    }

    [Fact]
    public async Task InitializeAsync_ResetsPreexistingUnversionedSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateUnknownSchemaAsync(tempStorage);

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal("Current", status.SchemaStatus);
        Assert.False(await tempStorage.TableExistsAsync("obsolete_data"));
        Assert.True(await tempStorage.TableExistsAsync("documents"));
        Assert.Single(tempStorage.ListBackupRoots());
    }

    [Fact]
    public async Task InitializeAsync_ResetsFutureSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateFutureSchemaAsync(tempStorage);

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteSchemaInitializer.CurrentSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal("Current", status.SchemaStatus);
        Assert.True(await tempStorage.TableExistsAsync("documents"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "document_uid"));
        Assert.Single(tempStorage.ListBackupRoots());
    }

    [Fact]
    public async Task GetStatusAsync_ReportsResetRequiredWithoutChangingPreexistingSchema()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateUnknownSchemaAsync(tempStorage);

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.GetStatusAsync();

        Assert.Equal("ResetRequired", status.SchemaStatus);
        Assert.True(await tempStorage.TableExistsAsync("obsolete_data"));
        Assert.Empty(tempStorage.ListBackupRoots());
    }

    private static async Task<int> ReadUserVersionAsync(TempStorage tempStorage)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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
    public async Task EmbeddingRepository_TracksQdrantIndexStatusByModel()
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
        await embeddings.MarkChunkIndexedAsync(
            firstModelChunks[0].Id,
            "embed-a",
            "hash-a",
            dimensions: 3,
            qdrantCollection: "onlyrag_3_test",
            qdrantPointId: firstModelChunks[0].Id.ToString());

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

    [Fact]
    public async Task DocumentRepository_SaveIngestedPageAsync_WritesValidChunkMetadataJson()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();
        SqliteDocumentRepository documents = new(tempStorage.CreateConnectionFactory());

        ImportedDocument document = await documents.CreateAsync(new CreateDocumentRecordRequest(
            "doc-metadata",
            "sample.txt",
            Path.Combine(tempStorage.Root, "sample.txt"),
            "sha-metadata",
            "text/plain",
            ".txt",
            12,
            DocumentStatus.Imported,
            PageCount: 0,
            CurrentJobId: null,
            LastError: null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
        const string contentHash = "hash-\"quoted\"\\slash";
        await documents.SaveIngestedPageAsync(
            document.Id,
            new IngestedDocumentPage(1, "alpha beta"),
            [new IngestedDocumentChunk(1, 1, 0, "alpha beta", 2, contentHash)],
            pageCount: 1);

        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT metadata_json FROM chunks WHERE document_id = $documentId;";
        command.AddParameter("$documentId", document.Id);

        string metadataJson = Assert.IsType<string>(await command.ExecuteScalarAsync());
        using JsonDocument metadata = JsonDocument.Parse(metadataJson);
        Assert.Equal(document.Id, metadata.RootElement.GetProperty("document_id").GetInt64());
        Assert.Equal(contentHash, metadata.RootElement.GetProperty("content_hash").GetString());
    }

}
