using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteMigrator
{
    private static async Task ApplySchemaVersion9Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "documents", "file_extension", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "documents", "current_job_id", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "chunks", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "embeddings", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "checkpoint_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "layout_metadata_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "machine_translated_text", "TEXT NULL", cancellationToken);

        await ExecuteInTransactionAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_chunks_content_hash ON chunks(content_hash);", cancellationToken);
        await ExecuteInTransactionAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_embeddings_content_hash ON embeddings(content_hash);", cancellationToken);
        await EnsureChunkFtsObjectsAsync(connection, transaction, textSearchBackend, cancellationToken);
    }

    private static Task ApplySchemaVersion10Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        return EnsureChunkFtsObjectsAsync(connection, transaction, textSearchBackend, cancellationToken);
    }

    private static async Task ApplySchemaVersion11Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await EnsureNoDuplicateDocumentHashesAsync(connection, transaction, cancellationToken);
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_sha256_not_null
            ON documents(sha256)
            WHERE sha256 IS NOT NULL;
            """,
            cancellationToken);
    }

    private static async Task ApplySchemaVersion12Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await SqliteStatusConstraints.ValidateExistingStatusesAsync(connection, transaction, cancellationToken);
        await SqliteStatusConstraints.CreateValidationTriggersAsync(connection, transaction, cancellationToken);
    }

    private static async Task EnsureNoDuplicateDocumentHashesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT sha256, COUNT(*) AS duplicate_count, GROUP_CONCAT(id, ',') AS ids
            FROM documents
            WHERE sha256 IS NOT NULL
            GROUP BY sha256
            HAVING COUNT(*) > 1
            ORDER BY duplicate_count DESC, sha256 ASC
            LIMIT 5;
            """;

        List<string> duplicates = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string sha256 = reader.GetString(0);
            long duplicateCount = reader.GetInt64(1);
            string ids = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            duplicates.Add($"{sha256} ({duplicateCount} rows: {ids})");
        }

        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "Database SQLite contiene documenti duplicati con lo stesso sha256. Risolvere i duplicati prima della migrazione. Hash duplicati: "
                + string.Join("; ", duplicates));
        }
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (await ColumnExistsAsync(connection, transaction, tableName, columnName, cancellationToken))
        {
            return;
        }

        await ExecuteInTransactionAsync(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
            cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task EnsureChunkFtsObjectsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        if (textSearchBackend == SqliteTextSearchBackend.None
            || await TableExistsAsync(connection, "chunks_fts", cancellationToken))
        {
            return;
        }

        await ExecuteInTransactionAsync(connection, transaction, BuildChunkFtsTriggerSql(textSearchBackend), cancellationToken);
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            """
            INSERT INTO chunks_fts(rowid, chunk_id, content)
            SELECT id, id, content FROM chunks;
            """,
            cancellationToken);
    }

    private static async Task ExecuteInTransactionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
