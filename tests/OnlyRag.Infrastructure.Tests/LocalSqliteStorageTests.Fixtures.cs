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
                max_retries INTEGER NOT NULL DEFAULT 5,
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

    private static async Task CreateVersion12SchemaMissingVersion9ColumnsAsync(TempStorage tempStorage)
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
                max_retries INTEGER NOT NULL DEFAULT 5,
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
            VALUES
                (9, '009_add_document_jobs_hashes_and_translation_layout', '2026-05-12T00:00:00.000Z'),
                (10, '010_add_fts4_keyword_search_fallback', '2026-05-12T00:00:00.000Z'),
                (11, '011_enforce_unique_document_hashes', '2026-05-12T00:00:00.000Z'),
                (12, '012_add_status_constraints', '2026-05-12T00:00:00.000Z');
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
