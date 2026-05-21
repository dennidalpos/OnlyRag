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
        Assert.True(status.Fts5Available || status.TechnicalNote is not null);
        if (status.Fts5Available || status.TechnicalNote?.Contains("FTS4", StringComparison.OrdinalIgnoreCase) == true)
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
        Assert.True(await tempStorage.IndexExistsAsync("ux_documents_sha256_not_null"));
    }

    [Fact]
    public async Task InitializeAsync_MigratesVersion8SchemaAndCreatesBackup()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateVersion8SchemaAsync(tempStorage);

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteMigrator.TargetSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal("Current", status.MigrationStatus);
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "file_extension"));
        Assert.True(await tempStorage.ColumnExistsAsync("documents", "current_job_id"));
        Assert.True(await tempStorage.ColumnExistsAsync("chunks", "content_hash"));
        Assert.True(await tempStorage.ColumnExistsAsync("embeddings", "content_hash"));
        Assert.True(await tempStorage.ColumnExistsAsync("jobs", "checkpoint_json"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "machine_translated_text"));
        Assert.True(await tempStorage.ColumnExistsAsync("translation_units", "layout_metadata_json"));

        string backupPath = Assert.Single(Directory.GetFiles(tempStorage.BackupDirectory, "*.db"));
        await using (SqliteConnection backupConnection = new($"Data Source={backupPath};Mode=ReadOnly;Pooling=False"))
        {
            await backupConnection.OpenAsync();
            await using SqliteCommand backupCommand = backupConnection.CreateCommand();
            backupCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
            Assert.Equal(8, Convert.ToInt32(await backupCommand.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesBackupBeforeFailedSupportedMigration()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await using (SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync())
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migrations (
                    version INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc TEXT NOT NULL
                );

                INSERT INTO schema_migrations(version, name, applied_at_utc)
                VALUES (8, '008_incomplete_fixture', '2026-05-12T00:00:00.000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        await Assert.ThrowsAsync<SqliteException>(() => storage.InitializeAsync());

        string backupPath = Assert.Single(Directory.GetFiles(tempStorage.BackupDirectory, "*.db"));
        Assert.True(File.Exists(backupPath));
        Assert.Equal(1, await CountRowsAsync(tempStorage, "schema_migrations", "version = $value", 8));
    }

    [Fact]
    public async Task InitializeAsync_RejectsExistingDuplicateDocumentHashesBeforeUniqueIndex()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateVersion10SchemaWithDuplicateDocumentHashesAsync(tempStorage);
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.InitializeAsync());

        Assert.Contains("documenti duplicati", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duplicate-sha", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await tempStorage.IndexExistsAsync("ux_documents_sha256_not_null"));
        Assert.Equal(1, await CountRowsAsync(tempStorage, "schema_migrations", "version = $value", 10));
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

        public string BackupDirectory => Path.Combine(Paths.DataDirectory, "backups");

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

        public async Task<bool> IndexExistsAsync(string indexName)
        {
            await using SqliteConnection connection = await CreateConnectionFactory().OpenConnectionAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$name", indexName);
            object? value = await command.ExecuteScalarAsync();
            return value is not null;
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

    private static async Task CreateVersion8SchemaAsync(TempStorage tempStorage)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_uid TEXT NOT NULL UNIQUE,
                original_file_name TEXT NOT NULL,
                original_path TEXT NOT NULL,
                sha256 TEXT NULL,
                mime_type TEXT NULL,
                file_size_bytes INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Imported',
                page_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                document_page_id INTEGER NULL,
                chunk_index INTEGER NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NULL,
                page_start INTEGER NULL,
                page_end INTEGER NULL,
                metadata_json TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                UNIQUE (document_id, chunk_index)
            );

            CREATE TABLE embeddings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                chunk_id INTEGER NOT NULL,
                model TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                distance_metric TEXT NOT NULL DEFAULT 'cosine',
                vector_blob BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                UNIQUE (chunk_id, model)
            );

            CREATE TABLE jobs (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                status TEXT NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                current_step TEXT NOT NULL DEFAULT '',
                payload_json TEXT NOT NULL DEFAULT '{}',
                error TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE translation_units (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                translation_id INTEGER NOT NULL,
                document_page_id INTEGER NULL,
                unit_index INTEGER NOT NULL,
                unit_kind TEXT NOT NULL DEFAULT 'paragraph',
                page_number INTEGER NULL,
                source_text TEXT NOT NULL,
                source_hash TEXT NOT NULL DEFAULT '',
                translated_text TEXT NULL,
                manually_edited INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Pending',
                validation_warnings TEXT NULL,
                error TEXT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                completed_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                UNIQUE (translation_id, unit_index)
            );

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (8, '008_previous_local_storage', '2026-05-12T00:00:00.000Z');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateVersion10SchemaWithDuplicateDocumentHashesAsync(TempStorage tempStorage)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_uid TEXT NOT NULL UNIQUE,
                original_file_name TEXT NOT NULL,
                original_path TEXT NOT NULL,
                sha256 TEXT NULL,
                mime_type TEXT NULL,
                file_extension TEXT NULL,
                file_size_bytes INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Imported',
                page_count INTEGER NOT NULL DEFAULT 0,
                current_job_id TEXT NULL,
                last_error TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            INSERT INTO documents (
                document_uid,
                original_file_name,
                original_path,
                sha256,
                mime_type,
                file_extension,
                file_size_bytes,
                status,
                page_count,
                created_at_utc,
                updated_at_utc
            )
            VALUES
                ('doc-a', 'a.txt', 'a.txt', 'duplicate-sha', 'text/plain', '.txt', 1, 'Imported', 0, '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'),
                ('doc-b', 'b.txt', 'b.txt', 'duplicate-sha', 'text/plain', '.txt', 1, 'Imported', 0, '2026-05-22T00:00:01.000Z', '2026-05-22T00:00:01.000Z');

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (10, '010_add_fts4_keyword_search_fallback', '2026-05-12T00:00:00.000Z');
            """;
        await command.ExecuteNonQueryAsync();
    }
}
