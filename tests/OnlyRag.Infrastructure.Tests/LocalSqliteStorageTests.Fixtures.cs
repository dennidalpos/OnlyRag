using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class LocalSqliteStorageTests
{
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

        public string BackupDirectory => Paths.BackupsDirectory;

        public static TempStorage Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));
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

        public string[] ListBackupRoots()
        {
            return Directory.Exists(BackupDirectory)
                ? Directory.GetDirectories(BackupDirectory, "reset-*")
                : [];
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

    private static async Task CreateVersionedSchemaAsync(TempStorage tempStorage)
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
                file_size_bytes INTEGER NOT NULL DEFAULT 0,
                status TEXT NOT NULL DEFAULT 'Imported',
                page_count INTEGER NOT NULL DEFAULT 0,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            INSERT INTO documents (
                document_uid,
                original_file_name,
                original_path,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                'old-doc',
                'old.txt',
                'old.txt',
                '2026-05-12T00:00:00.000Z',
                '2026-05-12T00:00:00.000Z'
            );

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (12, '012_add_status_constraints', '2026-05-12T00:00:00.000Z');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateUnknownSchemaAsync(TempStorage tempStorage)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE obsolete_data (id INTEGER PRIMARY KEY);";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task CreateFutureSchemaAsync(TempStorage tempStorage)
    {
        await using SqliteConnection connection = await tempStorage.CreateConnectionFactory().OpenConnectionAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA user_version = 99;
            CREATE TABLE documents (id INTEGER PRIMARY KEY);
            """;
        await command.ExecuteNonQueryAsync();
    }
}
