using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteSchemaInitializer
{
    private const int LegacySchemaMinimumSupportedVersion = 8;
    private const int LegacySchemaMaximumSupportedVersion = 12;

    private static async Task<int?> GetLegacySchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    private static async Task<SchemaMigrationResult> TryMigrateLegacySchemaAsync(
        SqliteConnection connection,
        int legacySchemaVersion,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (legacySchemaVersion < 9)
            {
                await ApplyLegacySchema9Async(connection, transaction, cancellationToken);
            }

            if (legacySchemaVersion < 10)
            {
                await ApplyLegacySchema10Async(connection, transaction, textSearchBackend, cancellationToken);
            }

            if (legacySchemaVersion < 11)
            {
                await ApplyLegacySchema11Async(connection, transaction, cancellationToken);
            }

            if (legacySchemaVersion < 12)
            {
                await ApplyLegacySchema12Async(connection, transaction, cancellationToken);
            }

            await FinalizeLegacySchemaAsync(connection, transaction, textSearchBackend, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new SchemaMigrationResult(true, null);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new SchemaMigrationResult(
                false,
                $"Migrazione SQLite legacy non completata: {ex.Message}. Nessun dato e stato eliminato automaticamente.");
        }
    }

    private static async Task ApplyLegacySchema9Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "documents", "file_extension", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "documents", "current_job_id", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "chunks", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "checkpoint_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "layout_metadata_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "machine_translated_text", "TEXT NULL", cancellationToken);
        await RecordLegacyMigrationAsync(connection, transaction, 9, "009_add_document_jobs_hashes_and_translation_layout", cancellationToken);
    }

    private static async Task ApplyLegacySchema10Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await EnsureChunkFtsSchemaAsync(connection, transaction, textSearchBackend, cancellationToken);
        await RecordLegacyMigrationAsync(connection, transaction, 10, "010_add_fts4_keyword_search_fallback", cancellationToken);
    }

    private static async Task ApplyLegacySchema11Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await DeduplicateDocumentHashesAsync(connection, transaction, cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            "CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_sha256_not_null ON documents(sha256) WHERE sha256 IS NOT NULL;",
            cancellationToken);
        await RecordLegacyMigrationAsync(connection, transaction, 11, "011_enforce_unique_document_hashes", cancellationToken);
    }

    private static async Task ApplyLegacySchema12Async(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await EnsureCurrentSupportTablesAsync(connection, transaction, cancellationToken);
        await AddCurrentCompatibilityColumnsAsync(connection, transaction, cancellationToken);
        await SqliteStatusConstraints.ValidateExistingStatusesAsync(connection, transaction, cancellationToken);
        await SqliteStatusConstraints.CreateValidationTriggersAsync(connection, transaction, cancellationToken);
        await RecordLegacyMigrationAsync(connection, transaction, 12, "012_add_status_constraints", cancellationToken);
    }

    private static async Task FinalizeLegacySchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await EnsureCurrentSupportTablesAsync(connection, transaction, cancellationToken);
        await AddCurrentCompatibilityColumnsAsync(connection, transaction, cancellationToken);
        await DeduplicateDocumentHashesAsync(connection, transaction, cancellationToken);
        await EnsureCurrentIndexesAsync(connection, transaction, cancellationToken);
        await EnsureChunkFtsSchemaAsync(connection, transaction, textSearchBackend, cancellationToken);
        await SqliteStatusConstraints.ValidateExistingStatusesAsync(connection, transaction, cancellationToken);
        await SqliteStatusConstraints.CreateValidationTriggersAsync(connection, transaction, cancellationToken);
        await ExecuteAsync(connection, transaction, "DROP TABLE IF EXISTS schema_migrations;", cancellationToken);
        await ExecuteAsync(connection, transaction, $"PRAGMA user_version = {CurrentSchemaVersion};", cancellationToken);
    }

    private static async Task AddCurrentCompatibilityColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await AddColumnIfMissingAsync(connection, transaction, "documents", "file_extension", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "documents", "current_job_id", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "chunks", "content_hash", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "checkpoint_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "jobs", "next_attempt_at_utc", "TEXT NULL", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "layout_metadata_json", "TEXT NOT NULL DEFAULT '{}'", cancellationToken);
        await AddColumnIfMissingAsync(connection, transaction, "translation_units", "machine_translated_text", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureCurrentSupportTablesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE TABLE IF NOT EXISTS documents (
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

            CREATE TABLE IF NOT EXISTS document_pages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                page_number INTEGER NOT NULL,
                render_path TEXT NULL,
                ocr_cache_path TEXT NULL,
                text_content TEXT NULL,
                ocr_status TEXT NULL,
                ocr_engine TEXT NULL,
                ocr_language TEXT NULL,
                ocr_confidence REAL NULL,
                ocr_boxes_json TEXT NULL,
                ocr_error TEXT NULL,
                ocr_completed_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
                UNIQUE (document_id, page_number)
            );

            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                document_page_id INTEGER NULL,
                chunk_index INTEGER NOT NULL,
                content TEXT NOT NULL,
                token_count INTEGER NULL,
                page_start INTEGER NULL,
                page_end INTEGER NULL,
                content_hash TEXT NOT NULL DEFAULT '',
                metadata_json TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
                FOREIGN KEY (document_page_id) REFERENCES document_pages(id) ON DELETE SET NULL,
                UNIQUE (document_id, chunk_index)
            );

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

            CREATE TABLE IF NOT EXISTS jobs (
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
                next_attempt_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chat_conversations (
                conversation_id TEXT PRIMARY KEY,
                title TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                model TEXT NULL,
                metadata_json TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (conversation_id) REFERENCES chat_conversations(conversation_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS translations (
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
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS translation_units (
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
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (translation_id) REFERENCES translations(id) ON DELETE CASCADE,
                FOREIGN KEY (document_page_id) REFERENCES document_pages(id) ON DELETE SET NULL,
                UNIQUE (translation_id, unit_index)
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                value_type TEXT NOT NULL DEFAULT 'string',
                updated_at_utc TEXT NOT NULL
            );

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

            CREATE TABLE IF NOT EXISTS ocr_cache (
                cache_key TEXT PRIMARY KEY,
                page_hash TEXT NOT NULL,
                engine_name TEXT NOT NULL,
                engine_version TEXT NOT NULL,
                language TEXT NOT NULL,
                preprocess_version TEXT NOT NULL,
                text_content TEXT NOT NULL,
                boxes_json TEXT NULL,
                confidence REAL NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task EnsureCurrentIndexesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            transaction,
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ux_documents_sha256_not_null ON documents(sha256) WHERE sha256 IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_documents_status_created ON documents(status, created_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_document_pages_document ON document_pages(document_id);
            CREATE INDEX IF NOT EXISTS idx_document_pages_ocr ON document_pages(document_id, ocr_status, page_number);
            CREATE INDEX IF NOT EXISTS idx_chunks_document ON chunks(document_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_page ON chunks(document_page_id);
            CREATE INDEX IF NOT EXISTS idx_chunks_document_ordinal ON chunks(document_id, chunk_index);
            CREATE INDEX IF NOT EXISTS idx_chunks_content_hash ON chunks(content_hash);
            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_chunk ON chunk_vector_index_status(chunk_id);
            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_model_chunk ON chunk_vector_index_status(model, chunk_id);
            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_content_hash ON chunk_vector_index_status(content_hash);
            CREATE INDEX IF NOT EXISTS idx_chunk_vector_index_status_collection ON chunk_vector_index_status(qdrant_collection);
            CREATE INDEX IF NOT EXISTS idx_jobs_status_priority ON jobs(status, priority DESC, created_at_utc);
            CREATE INDEX IF NOT EXISTS idx_jobs_pending_due ON jobs(status, next_attempt_at_utc, priority DESC, created_at_utc);
            CREATE INDEX IF NOT EXISTS idx_jobs_updated_at ON jobs(updated_at_utc);
            CREATE INDEX IF NOT EXISTS idx_chat_messages_conversation ON chat_messages(conversation_id, id);
            CREATE INDEX IF NOT EXISTS idx_translations_document ON translations(document_id, created_at_utc DESC);
            CREATE INDEX IF NOT EXISTS idx_translations_job ON translations(job_id);
            CREATE INDEX IF NOT EXISTS idx_translation_units_translation ON translation_units(translation_id, unit_index);
            CREATE INDEX IF NOT EXISTS idx_translation_units_status ON translation_units(translation_id, status, unit_index);
            CREATE INDEX IF NOT EXISTS idx_generated_images_created_at ON generated_images(created_at_utc DESC, id DESC);
            CREATE INDEX IF NOT EXISTS idx_ocr_cache_lookup
            ON ocr_cache(page_hash, engine_name, engine_version, language, preprocess_version);
            """,
            cancellationToken);
    }

    private static async Task EnsureChunkFtsSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        if (textSearchBackend == SqliteTextSearchBackend.None
            || await TableExistsAsync(connection, transaction, "chunks_fts", cancellationToken))
        {
            return;
        }

        string createTableSql = textSearchBackend == SqliteTextSearchBackend.Fts5
            ? """
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
                chunk_id UNINDEXED,
                content
            );
            """
            : """
            CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts4(
                chunk_id,
                content,
                notindexed=chunk_id
            );
            """;

        await ExecuteAsync(
            connection,
            transaction,
            $$"""
            {{createTableSql}}

            INSERT INTO chunks_fts(rowid, chunk_id, content)
            SELECT id, id, content FROM chunks
            WHERE NOT EXISTS (SELECT 1 FROM chunks_fts WHERE chunks_fts.rowid = chunks.id);

            CREATE TRIGGER IF NOT EXISTS chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO chunks_fts(rowid, chunk_id, content)
                VALUES (new.id, new.id, new.content);
            END;

            CREATE TRIGGER IF NOT EXISTS chunks_ad AFTER DELETE ON chunks BEGIN
                DELETE FROM chunks_fts WHERE rowid = old.id;
            END;

            CREATE TRIGGER IF NOT EXISTS chunks_au AFTER UPDATE OF content ON chunks BEGIN
                DELETE FROM chunks_fts WHERE rowid = old.id;
                INSERT INTO chunks_fts(rowid, chunk_id, content)
                VALUES (new.id, new.id, new.content);
            END;
            """,
            cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, tableName, cancellationToken)
            || await ColumnExistsAsync(connection, transaction, tableName, columnName, cancellationToken))
        {
            return;
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
            cancellationToken);
    }

    private static async Task DeduplicateDocumentHashesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, "documents", cancellationToken)
            || !await ColumnExistsAsync(connection, transaction, "documents", "sha256", cancellationToken))
        {
            return;
        }

        await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE documents
            SET sha256 = NULL
            WHERE sha256 IS NOT NULL
              AND id NOT IN (
                  SELECT MIN(id)
                  FROM documents
                  WHERE sha256 IS NOT NULL
                  GROUP BY sha256
              );
            """,
            cancellationToken);
    }

    private static Task RecordLegacyMigrationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        string name,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            connection,
            transaction,
            $"""
            INSERT OR IGNORE INTO schema_migrations(version, name, applied_at_utc)
            VALUES ({version}, '{name}', '{DateTimeOffset.UtcNow:O}');
            """,
            cancellationToken);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.AddParameter("$name", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
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

    private static async Task ExecuteAsync(
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

    private sealed record SchemaMigrationResult(bool Migrated, string? TechnicalNote);
}
