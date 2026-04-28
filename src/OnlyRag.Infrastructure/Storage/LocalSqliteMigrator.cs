using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteMigrator
{
    public const int TargetSchemaVersion = 9;
    private const string InitialSchemaName = "009_fresh_local_storage";
    private const string Fts5UnavailableNote = "TODO: FTS5 is not available in the active SQLite provider; add a supported SQLite bundle or a fallback full-text index before enabling search.";

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly ISqliteConnectionFactory connectionFactory;

    public LocalSqliteMigrator(
        LocalSqliteStoreDescriptor descriptor,
        ISqliteConnectionFactory connectionFactory)
    {
        this.descriptor = descriptor;
        this.connectionFactory = connectionFactory;
    }

    public async Task<StorageStatusResponse> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);

        bool fts5Available = await DetectFts5Async(connection, cancellationToken);
        int currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
        if (currentVersion == 0)
        {
            if (await UserSchemaExistsAsync(connection, cancellationToken))
            {
                throw new InvalidOperationException(
                    "Database SQLite esistente senza schema OnlyRag supportato. OnlyRag e trattata come nuova app e non esegue migrazioni dati.");
            }

            await ApplyFreshSchemaAsync(connection, fts5Available, cancellationToken);
            currentVersion = TargetSchemaVersion;
        }
        else if (currentVersion != TargetSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Schema SQLite OnlyRag non supportato: versione {currentVersion}, attesa {TargetSchemaVersion}. Le migrazioni dati esistenti non sono supportate.");
        }

        return BuildStatus(currentVersion, fts5Available);
    }

    public async Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(descriptor.Paths.DatabasePath))
        {
            return new StorageStatusResponse(
                descriptor.ProviderName,
                descriptor.Paths.DatabasePath,
                DatabaseExists: false,
                CurrentSchemaVersion: 0,
                TargetSchemaVersion,
                MigrationStatus: "NotInitialized",
                Fts5Available: false,
                TechnicalNote: null);
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        bool migrationTableExists = await TableExistsAsync(connection, "schema_migrations", cancellationToken);
        int currentVersion = migrationTableExists
            ? await GetCurrentSchemaVersionAsync(connection, cancellationToken)
            : 0;
        bool fts5Available = await DetectFts5Async(connection, cancellationToken);

        return BuildStatus(currentVersion, fts5Available);
    }

    private StorageStatusResponse BuildStatus(int currentVersion, bool fts5Available)
    {
        string migrationStatus = currentVersion switch
        {
            0 => "NotInitialized",
            TargetSchemaVersion => "Current",
            _ => "Unsupported"
        };

        string? technicalNote = fts5Available
            ? null
            : Fts5UnavailableNote;
        if (migrationStatus == "Unsupported")
        {
            technicalNote = "La versione schema locale non e supportata: il progetto e trattato come nuova app e non esegue migrazioni dati.";
        }

        return new StorageStatusResponse(
            descriptor.ProviderName,
            descriptor.Paths.DatabasePath,
            File.Exists(descriptor.Paths.DatabasePath),
            currentVersion,
            TargetSchemaVersion,
            migrationStatus,
            fts5Available,
            technicalNote);
    }

    private static async Task EnsureMigrationTableAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteNonQueryAsync(
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            """,
            cancellationToken);
    }

    private static async Task<int> GetCurrentSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task<bool> TableExistsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
        command.AddParameter("$name", tableName);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static async Task<bool> UserSchemaExistsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT 1
            FROM sqlite_master
            WHERE name NOT LIKE 'sqlite_%'
              AND name <> 'schema_migrations'
            LIMIT 1;
            """;
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static async Task<bool> DetectFts5Async(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ExecuteNonQueryAsync(
                "CREATE VIRTUAL TABLE temp.__onlyrag_fts5_probe USING fts5(content);",
                cancellationToken);
            await connection.ExecuteNonQueryAsync(
                "DROP TABLE temp.__onlyrag_fts5_probe;",
                cancellationToken);
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static async Task ApplyFreshSchemaAsync(
        SqliteConnection connection,
        bool fts5Available,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildFreshSchemaSql(fts5Available);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string BuildFreshSchemaSql(bool fts5Available)
    {
        string ftsSql = BuildChunkFtsTriggerSql(fts5Available);

        return $$"""
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

            CREATE TABLE document_pages (
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

            CREATE TABLE chunks (
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

            CREATE TABLE embeddings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                chunk_id INTEGER NOT NULL,
                model TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                distance_metric TEXT NOT NULL DEFAULT 'cosine',
                content_hash TEXT NOT NULL DEFAULT '',
                vector_blob BLOB NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (chunk_id) REFERENCES chunks(id) ON DELETE CASCADE,
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
                checkpoint_json TEXT NOT NULL DEFAULT '{}',
                error TEXT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                max_retries INTEGER NOT NULL DEFAULT 3,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE chat_conversations (
                conversation_id TEXT PRIMARY KEY,
                title TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE chat_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                model TEXT NULL,
                metadata_json TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (conversation_id) REFERENCES chat_conversations(conversation_id) ON DELETE CASCADE
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
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
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
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (translation_id) REFERENCES translations(id) ON DELETE CASCADE,
                FOREIGN KEY (document_page_id) REFERENCES document_pages(id) ON DELETE SET NULL,
                UNIQUE (translation_id, unit_index)
            );

            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                value_type TEXT NOT NULL DEFAULT 'string',
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE ocr_cache (
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

            CREATE INDEX idx_documents_sha256 ON documents(sha256);
            CREATE INDEX idx_documents_status_created ON documents(status, created_at_utc DESC);
            CREATE INDEX idx_document_pages_document ON document_pages(document_id);
            CREATE INDEX idx_document_pages_ocr ON document_pages(document_id, ocr_status, page_number);
            CREATE INDEX idx_chunks_document ON chunks(document_id);
            CREATE INDEX idx_chunks_page ON chunks(document_page_id);
            CREATE INDEX idx_chunks_document_ordinal ON chunks(document_id, chunk_index);
            CREATE INDEX idx_chunks_content_hash ON chunks(content_hash);
            CREATE INDEX idx_embeddings_chunk ON embeddings(chunk_id);
            CREATE INDEX idx_embeddings_model_chunk ON embeddings(model, chunk_id);
            CREATE INDEX idx_embeddings_content_hash ON embeddings(content_hash);
            CREATE INDEX idx_jobs_status_priority ON jobs(status, priority DESC, created_at_utc);
            CREATE INDEX idx_jobs_updated_at ON jobs(updated_at_utc);
            CREATE INDEX idx_chat_messages_conversation ON chat_messages(conversation_id, id);
            CREATE INDEX idx_translations_document ON translations(document_id, created_at_utc DESC);
            CREATE INDEX idx_translations_job ON translations(job_id);
            CREATE INDEX idx_translation_units_translation ON translation_units(translation_id, unit_index);
            CREATE INDEX idx_translation_units_status ON translation_units(translation_id, status, unit_index);
            CREATE INDEX idx_ocr_cache_lookup
            ON ocr_cache(page_hash, engine_name, engine_version, language, preprocess_version);
            {{ftsSql}}

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (9, '{{InitialSchemaName}}', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
    }

    private static string BuildChunkFtsTriggerSql(bool fts5Available)
    {
        return fts5Available
            ? """

            CREATE VIRTUAL TABLE chunks_fts USING fts5(
                chunk_id UNINDEXED,
                content,
                tokenize = 'unicode61'
            );

            CREATE TRIGGER chunks_ai AFTER INSERT ON chunks BEGIN
                INSERT INTO chunks_fts(rowid, chunk_id, content)
                VALUES (new.id, new.id, new.content);
            END;

            CREATE TRIGGER chunks_ad AFTER DELETE ON chunks BEGIN
                DELETE FROM chunks_fts WHERE rowid = old.id;
            END;

            CREATE TRIGGER chunks_au AFTER UPDATE OF content ON chunks BEGIN
                DELETE FROM chunks_fts WHERE rowid = old.id;
                INSERT INTO chunks_fts(rowid, chunk_id, content)
                VALUES (new.id, new.id, new.content);
            END;
            """
            : """

            -- TODO: FTS5 is not available in the active SQLite provider.
            -- Add a supported SQLite bundle or a fallback full-text index before enabling search.
            """;
    }
}
