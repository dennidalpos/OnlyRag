using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SqliteStatusConstraintTests
{
    [Fact]
    public void JobStatusSqlPredicates_UsePersistedEnumNames()
    {
        string allJobStatuses = string.Join(", ", Enum.GetNames<JobStatus>().Select(status => $"'{status}'"));

        Assert.Equal(SqliteStatusConstraints.JobStatusPredicate, allJobStatuses);
        Assert.Equal(
            "status IN ('Pending', 'Running')",
            SqliteStatusConstraints.BuildJobStatusInPredicate([JobStatus.Pending, JobStatus.Running]));
        Assert.Equal(
            "status NOT IN ('Running', 'Pausing', 'Pending')",
            SqliteStatusConstraints.BuildJobStatusNotInPredicate(
                [JobStatus.Running, JobStatus.Pausing, JobStatus.Pending]));
        Assert.Equal(
            "status = 'Pending'",
            SqliteStatusConstraints.BuildJobStatusEqualsPredicate(JobStatus.Pending));
    }

    [Fact]
    public async Task InitializeAsync_FreshSchemaRejectsInvalidJobAndTranslationStatuses()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        await storage.InitializeAsync();
        await SeedValidDocumentAndTranslationAsync(tempStorage);

        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            """
            INSERT INTO jobs (
                id, type, status, created_at_utc, updated_at_utc
            )
            VALUES (
                'job-invalid', 'test', 'NotAStatus', '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );
            """));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            """
            INSERT INTO translations (
                document_id, target_language, model, status, created_at_utc, updated_at_utc
            )
            VALUES (
                1, 'English', 'model', 'NotAStatus', '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );
            """));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            """
            INSERT INTO translation_units (
                translation_id, unit_index, source_text, status, created_at_utc, updated_at_utc
            )
            VALUES (
                1, 1, 'source', 'NotAStatus', '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );
            """));
    }

    [Fact]
    public async Task TranslationRepository_RejectsUnsupportedTranslationStatus()
    {
        using TempStorage tempStorage = TempStorage.Create();
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();
        await storage.InitializeAsync();
        await SeedValidDocumentAndTranslationAsync(tempStorage);
        SqliteTranslationRepository repository = new(tempStorage.CreateConnectionFactory());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.UpdateTranslationJobAsync(1, "job-1", "NotAStatus", null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            repository.RefreshProgressAsync(1, "NotAStatus", null));
    }

    [Fact]
    public async Task InitializeAsync_RejectsExistingInvalidStatusesBeforeStatusConstraintMigration()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateVersion11SchemaWithInvalidStatusesAsync(tempStorage);
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => storage.InitializeAsync());

        Assert.Contains("status non validi", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("jobs.status='NotAStatus'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("translations.status='NotAStatus'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("translation_units.status='NotAStatus'", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, await CountRowsAsync(tempStorage, "schema_migrations", "version = $value", 11));
    }

    [Fact]
    public async Task InitializeAsync_MigratesVersion11SchemaAndRejectsFutureInvalidStatuses()
    {
        using TempStorage tempStorage = TempStorage.Create();
        await CreateVersion11SchemaWithValidStatusesAsync(tempStorage);
        LocalSqliteStorageService storage = tempStorage.CreateStorageService();

        StorageStatusResponse status = await storage.InitializeAsync();

        Assert.Equal(LocalSqliteMigrator.TargetSchemaVersion, status.CurrentSchemaVersion);
        Assert.Equal(1, await CountRowsAsync(tempStorage, "schema_migrations", "version = $value", LocalSqliteMigrator.TargetSchemaVersion));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            "UPDATE jobs SET status = 'NotAStatus' WHERE id = 'job-valid';"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            "UPDATE translations SET status = 'NotAStatus' WHERE id = 1;"));
        await Assert.ThrowsAsync<SqliteException>(() => ExecuteRawAsync(
            tempStorage,
            "UPDATE translation_units SET status = 'NotAStatus' WHERE id = 1;"));
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
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.StatusConstraint.Tests", Guid.NewGuid().ToString("N"));
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

    private static async Task ExecuteRawAsync(TempStorage tempStorage, string commandText)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SeedValidDocumentAndTranslationAsync(TempStorage tempStorage)
    {
        await ExecuteRawAsync(
            tempStorage,
            """
            INSERT INTO documents (
                document_uid,
                original_file_name,
                original_path,
                file_size_bytes,
                status,
                page_count,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                'doc-status',
                'status.txt',
                'status.txt',
                1,
                'Imported',
                0,
                '2026-05-22T00:00:00.000Z',
                '2026-05-22T00:00:00.000Z'
            );

            INSERT INTO translations (
                document_id,
                target_language,
                model,
                status,
                unit_count,
                completed_unit_count,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                1,
                'English',
                'model',
                'Queued',
                1,
                0,
                '2026-05-22T00:00:00.000Z',
                '2026-05-22T00:00:00.000Z'
            );
            """);
    }

    private static Task CreateVersion11SchemaWithInvalidStatusesAsync(TempStorage tempStorage)
    {
        return CreateVersion11StatusSchemaAsync(tempStorage, "NotAStatus", "NotAStatus", "NotAStatus");
    }

    private static Task CreateVersion11SchemaWithValidStatusesAsync(TempStorage tempStorage)
    {
        return CreateVersion11StatusSchemaAsync(tempStorage, "Pending", "Queued", "Pending");
    }

    private static async Task CreateVersion11StatusSchemaAsync(
        TempStorage tempStorage,
        string jobStatus,
        string translationStatus,
        string translationUnitStatus)
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

            CREATE TABLE jobs (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                status TEXT NOT NULL,
                priority INTEGER NOT NULL DEFAULT 0,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                current_step TEXT NOT NULL DEFAULT '',
                payload_json TEXT NOT NULL DEFAULT '{}',
                checkpoint_json TEXT NOT NULL DEFAULT '{}',
                error TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 5,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE translations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                source_language TEXT NOT NULL DEFAULT 'auto',
                target_language TEXT NOT NULL,
                model TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL,
                job_id TEXT NULL,
                unit_count INTEGER NOT NULL DEFAULT 0,
                completed_unit_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
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
                layout_metadata_json TEXT NOT NULL DEFAULT '{}',
                machine_translated_text TEXT NULL,
                translated_text TEXT NULL,
                manually_edited INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Pending',
                validation_warnings TEXT NULL,
                error TEXT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                completed_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            INSERT INTO jobs (
                id, type, status, created_at_utc, updated_at_utc
            )
            VALUES (
                'job-valid', 'test', $jobStatus, '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );

            INSERT INTO translations (
                document_id, target_language, model, status, unit_count, created_at_utc, updated_at_utc
            )
            VALUES (
                1, 'English', 'model', $translationStatus, 1, '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );

            INSERT INTO translation_units (
                translation_id, unit_index, source_text, status, created_at_utc, updated_at_utc
            )
            VALUES (
                1, 0, 'source', $translationUnitStatus, '2026-05-22T00:00:00.000Z', '2026-05-22T00:00:00.000Z'
            );

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (11, '011_enforce_unique_document_hashes', '2026-05-22T00:00:00.000Z');
            """;
        command.Parameters.AddWithValue("$jobStatus", jobStatus);
        command.Parameters.AddWithValue("$translationStatus", translationStatus);
        command.Parameters.AddWithValue("$translationUnitStatus", translationUnitStatus);
        await command.ExecuteNonQueryAsync();
    }
}
