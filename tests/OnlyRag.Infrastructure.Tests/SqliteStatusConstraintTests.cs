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
            LocalSqliteSchemaInitializer initializer = new(Descriptor, connectionFactory);
            return new LocalSqliteStorageService(Descriptor, initializer);
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

}
