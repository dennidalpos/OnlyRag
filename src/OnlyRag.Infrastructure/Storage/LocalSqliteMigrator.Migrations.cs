using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteMigrator
{
    private static readonly (string TableName, string ColumnName)[] SchemaVersion9CompatibilityColumns =
    [
        ("documents", "file_extension"),
        ("documents", "current_job_id"),
        ("chunks", "content_hash"),
        ("jobs", "checkpoint_json"),
        ("translation_units", "layout_metadata_json"),
        ("translation_units", "machine_translated_text")
    ];

    private static async Task ApplySchemaVersion9Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "documents", "file_extension", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "documents", "current_job_id", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "chunks", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        if (await TableExistsAsync(connection, "embeddings", cancellationToken))
        {
            await AddColumnIfMissingAsync(connection, transaction, "embeddings", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        }
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "checkpoint_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "layout_metadata_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "machine_translated_text", "TEXT NULL", cancellationToken);

        await ExecuteInTransactionAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_chunks_content_hash ON chunks(content_hash);", cancellationToken);
        if (await TableExistsAsync(connection, "embeddings", cancellationToken))
        {
            await ExecuteInTransactionAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_embeddings_content_hash ON embeddings(content_hash);", cancellationToken);
        }
        await EnsureChunkFtsObjectsAsync(connection, transaction, textSearchBackend, cancellationToken);
    }

    private static async Task<bool> SchemaVersion9CompatibilityRepairRequiredAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        foreach ((string tableName, string columnName) in SchemaVersion9CompatibilityColumns)
        {
            if (await TableExistsAsync(connection, tableName, cancellationToken)
                && !await ColumnExistsAsync(connection, tableName, columnName, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task ApplySchemaVersion9CompatibilityRepairAsync(
        SqliteConnection connection,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ApplySchemaVersion9Async(connection, transaction, textSearchBackend, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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

    private static async Task ApplySchemaVersion13Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS chunk_vector_index_status (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                chunk_id INTEGER NOT NULL,
                model TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                content_hash TEXT NOT NULL DEFAULT '',
                qdrant_collection TEXT NOT NULL,
                qdrant_point_id TEXT NOT NULL,
                indexed_at_utc TEXT NULL,
                status TEXT NOT NULL DEFAULT 'Pending',
                last_error TEXT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (chunk_id) REFERENCES chunks(id) ON DELETE CASCADE,
                UNIQUE (chunk_id, model)
            );

            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_chunk
            ON chunk_vector_index_status(chunk_id);

            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_model_chunk
            ON chunk_vector_index_status(model, chunk_id);

            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_content_hash
            ON chunk_vector_index_status(content_hash);

            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_collection
            ON chunk_vector_index_status(qdrant_collection);
            """,
            cancellationToken);

        if (await TableExistsAsync(connection, "embeddings", cancellationToken))
        {
            await ExecuteInTransactionAsync(
                connection,
                transaction,
                """
                UPDATE documents
                SET status = 'RequiresEmbeddingRebuild',
                    current_job_id = NULL,
                    last_error = 'Embedding legacy SQLite eliminati durante migrazione v13: ricostruire indice Qdrant.',
                    updated_at_utc = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                WHERE id IN (
                    SELECT DISTINCT c.document_id
                    FROM chunks AS c
                    INNER JOIN embeddings AS e ON e.chunk_id = c.id
                );

                DROP TABLE embeddings;
                """,
                cancellationToken);
        }
    }

    private static async Task ApplySchemaVersion14Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "next_attempt_at_utc", "TEXT NULL", cancellationToken);
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            """
            CREATE INDEX IF NOT EXISTS idx_jobs_pending_due
            ON jobs(status, next_attempt_at_utc, priority DESC, created_at_utc);
            """,
            cancellationToken);
    }

    private static async Task ApplySchemaVersion15Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await ExecuteInTransactionAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS generated_images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider TEXT NOT NULL,
                prompt TEXT NOT NULL,
                negative_prompt TEXT NULL,
                model TEXT NULL,
                width INTEGER NOT NULL,
                height INTEGER NOT NULL,
                steps INTEGER NOT NULL,
                batch_size INTEGER NOT NULL,
                seed INTEGER NULL,
                file_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                mime_type TEXT NOT NULL,
                file_size_bytes INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_generated_images_created_at
            ON generated_images(created_at_utc DESC, id DESC);
            """,
            cancellationToken);
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
        return await ColumnExistsCoreAsync(connection, transaction, tableName, columnName, cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        return await ColumnExistsCoreAsync(connection, transaction: null, tableName, columnName, cancellationToken);
    }

    private static async Task<bool> ColumnExistsCoreAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

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
