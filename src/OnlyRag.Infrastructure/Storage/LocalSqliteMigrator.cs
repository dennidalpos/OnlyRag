using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteMigrator
{
    public const int TargetSchemaVersion = 11;
    private const string InitialSchemaName = "011_fresh_local_storage";
    private const string BackupDirectoryName = "backups";
    private const string FtsUnavailableNote = "No SQLite FTS module is available in the active SQLite provider; keyword search is disabled until FTS5 or FTS4 is available.";

    private static readonly IReadOnlyList<SqliteSchemaMigration> Migrations =
    [
        new(9, "009_add_document_jobs_hashes_and_translation_layout", ApplySchemaVersion9Async),
        new(10, "010_add_fts4_keyword_search_fallback", ApplySchemaVersion10Async),
        new(11, "011_enforce_unique_document_hashes", ApplySchemaVersion11Async)
    ];

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
        SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
        bool migrationTableExists = await TableExistsAsync(connection, "schema_migrations", cancellationToken);
        if (!migrationTableExists && await UserSchemaExistsAsync(connection, cancellationToken))
        {
            throw new InvalidOperationException(
                "Database SQLite esistente senza schema OnlyRag supportato. OnlyRag e trattata come nuova app e non esegue migrazioni dati.");
        }

        await EnsureMigrationTableAsync(connection, cancellationToken);
        int currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
        if (currentVersion == 0)
        {
            await ApplyFreshSchemaAsync(connection, textSearchBackend, cancellationToken);
            currentVersion = TargetSchemaVersion;
        }
        else if (currentVersion < TargetSchemaVersion)
        {
            await BackupDatabaseAsync(connection, currentVersion, TargetSchemaVersion, cancellationToken);
            await ApplyPendingMigrationsAsync(connection, currentVersion, textSearchBackend, cancellationToken);
            currentVersion = await GetCurrentSchemaVersionAsync(connection, cancellationToken);
        }
        else if (currentVersion > TargetSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Schema SQLite OnlyRag non supportato: versione {currentVersion}, attesa {TargetSchemaVersion}. Avviare una versione di OnlyRag compatibile o ripristinare un backup.");
        }

        return BuildStatus(currentVersion, textSearchBackend);
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
        SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);

        return BuildStatus(currentVersion, textSearchBackend);
    }

    private StorageStatusResponse BuildStatus(int currentVersion, SqliteTextSearchBackend textSearchBackend)
    {
        string migrationStatus = currentVersion switch
        {
            0 => "NotInitialized",
            TargetSchemaVersion => "Current",
            < TargetSchemaVersion => "MigrationRequired",
            _ => "Unsupported"
        };

        string? technicalNote = textSearchBackend switch
        {
            SqliteTextSearchBackend.Fts5 => null,
            SqliteTextSearchBackend.Fts4 => "SQLite FTS5 is unavailable; keyword search uses the indexed SQLite FTS4 fallback.",
            _ => FtsUnavailableNote
        };
        if (migrationStatus == "Unsupported")
        {
            technicalNote = "La versione schema locale e piu recente di questa applicazione. Avviare una versione compatibile o ripristinare un backup.";
        }
        else if (migrationStatus == "MigrationRequired")
        {
            technicalNote = "La versione schema locale richiede migrazione. OnlyRag crea un backup prima di applicare aggiornamenti schema supportati.";
        }

        return new StorageStatusResponse(
            descriptor.ProviderName,
            descriptor.Paths.DatabasePath,
            File.Exists(descriptor.Paths.DatabasePath),
            currentVersion,
            TargetSchemaVersion,
            migrationStatus,
            textSearchBackend == SqliteTextSearchBackend.Fts5,
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

    private static async Task<SqliteTextSearchBackend> DetectTextSearchBackendAsync(
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
            return SqliteTextSearchBackend.Fts5;
        }
        catch (SqliteException)
        {
        }

        try
        {
            await connection.ExecuteNonQueryAsync(
                "CREATE VIRTUAL TABLE temp.__onlyrag_fts4_probe USING fts4(content);",
                cancellationToken);
            await connection.ExecuteNonQueryAsync(
                "DROP TABLE temp.__onlyrag_fts4_probe;",
                cancellationToken);
            return SqliteTextSearchBackend.Fts4;
        }
        catch (SqliteException)
        {
            return SqliteTextSearchBackend.None;
        }
    }

    private static async Task ApplyFreshSchemaAsync(
        SqliteConnection connection,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = BuildFreshSchemaSql(textSearchBackend);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task BackupDatabaseAsync(
        SqliteConnection connection,
        int fromVersion,
        int toVersion,
        CancellationToken cancellationToken)
    {
        string databasePath = descriptor.Paths.DatabasePath;
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException("Database SQLite locale non trovato prima della migrazione.");
        }

        string backupDirectory = Path.Combine(descriptor.Paths.DataDirectory, BackupDirectoryName);
        Directory.CreateDirectory(backupDirectory);

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        string databaseName = Path.GetFileNameWithoutExtension(databasePath);
        string backupPath = Path.Combine(
            backupDirectory,
            $"{databaseName}.v{fromVersion}-to-v{toVersion}.{timestamp}.db");

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "VACUUM main INTO $backupPath;";
        command.AddParameter("$backupPath", backupPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ApplyPendingMigrationsAsync(
        SqliteConnection connection,
        int currentVersion,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        int version = currentVersion;
        foreach (SqliteSchemaMigration migration in Migrations.Where(migration => migration.Version > currentVersion))
        {
            if (migration.Version != version + 1)
            {
                throw new InvalidOperationException(
                    $"Migrazione SQLite OnlyRag mancante da versione {version} a {migration.Version}.");
            }

            await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                await migration.ApplyAsync(connection, transaction, textSearchBackend, cancellationToken);
                await InsertMigrationRecordAsync(connection, transaction, migration, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                version = migration.Version;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        if (version != TargetSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Schema SQLite OnlyRag non supportato: versione {currentVersion}, attesa {TargetSchemaVersion}. Nessun percorso di migrazione completo disponibile.");
        }
    }

    private static async Task InsertMigrationRecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SqliteSchemaMigration migration,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES ($version, $name, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
        command.AddParameter("$version", migration.Version);
        command.AddParameter("$name", migration.Name);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

    private static string BuildFreshSchemaSql(SqliteTextSearchBackend textSearchBackend)
    {
        string ftsSql = BuildChunkFtsTriggerSql(textSearchBackend);

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

            CREATE UNIQUE INDEX ux_documents_sha256_not_null ON documents(sha256) WHERE sha256 IS NOT NULL;
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
            VALUES (11, '{{InitialSchemaName}}', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            """;
    }

    private static string BuildChunkFtsTriggerSql(SqliteTextSearchBackend textSearchBackend)
    {
        string createTableSql = textSearchBackend switch
        {
            SqliteTextSearchBackend.Fts5 => """
            CREATE VIRTUAL TABLE chunks_fts USING fts5(
                chunk_id UNINDEXED,
                content
            );
            """,
            SqliteTextSearchBackend.Fts4 => """
            CREATE VIRTUAL TABLE chunks_fts USING fts4(
                chunk_id,
                content,
                notindexed=chunk_id
            );
            """,
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(createTableSql)
            ? """

            -- No SQLite FTS module is available in the active provider.
            """
            : $$"""

            {{createTableSql}}

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
            """;
    }

    private sealed record SqliteSchemaMigration(
        int Version,
        string Name,
        Func<SqliteConnection, SqliteTransaction, SqliteTextSearchBackend, CancellationToken, Task> ApplyAsync);

    private enum SqliteTextSearchBackend
    {
        None,
        Fts4,
        Fts5
    }
}
