using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteSchemaInitializer
{
    public const int CurrentSchemaVersion = 4;
    private const string FtsUnavailableNote = "SQLite FTS5 is unavailable in the active SQLite provider; keyword search is disabled.";

    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly ISqliteConnectionFactory connectionFactory;

    public LocalSqliteSchemaInitializer(
        LocalSqliteStoreDescriptor descriptor,
        ISqliteConnectionFactory connectionFactory)
    {
        this.descriptor = descriptor;
        this.connectionFactory = connectionFactory;
    }

    public async Task<StorageStatusResponse> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(descriptor.Paths.DatabasePath))
        {
            return await CreateFreshSchemaAsync(schemaTechnicalNote: null, cancellationToken);
        }

        string resetReason;
        try
        {
            await using SqliteConnection existingConnection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            SqliteTextSearchBackend existingTextSearchBackend = await DetectTextSearchBackendAsync(existingConnection, cancellationToken);
            int existingVersion = await GetUserVersionAsync(existingConnection, cancellationToken);
            SchemaInspection inspection = await InspectSchemaAsync(existingConnection, existingVersion, cancellationToken);

            if (inspection.Status == "Current")
            {
                return BuildStatus(existingVersion, existingTextSearchBackend);
            }

            resetReason = inspection.TechnicalNote ?? "Il database locale non corrisponde allo schema fresh corrente.";
        }
        catch (SqliteException ex)
        {
            resetReason = $"Database locale non leggibile: {ex.Message}";
        }

        return await ResetAndCreateFreshSchemaAsync(resetReason, cancellationToken);
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
                TargetSchemaVersion: CurrentSchemaVersion,
                SchemaStatus: "NotInitialized",
                Fts5Available: false,
                TechnicalNote: null);
        }

        try
        {
            await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
            int currentVersion = await GetUserVersionAsync(connection, cancellationToken);
            SchemaInspection inspection = await InspectSchemaAsync(connection, currentVersion, cancellationToken);

            return inspection.Status == "Current"
                ? BuildStatus(currentVersion, textSearchBackend)
                : BuildStatus(currentVersion, textSearchBackend, inspection.Status, inspection.TechnicalNote);
        }
        catch (SqliteException ex)
        {
            return new StorageStatusResponse(
                descriptor.ProviderName,
                descriptor.Paths.DatabasePath,
                DatabaseExists: true,
                CurrentSchemaVersion: 0,
                TargetSchemaVersion: CurrentSchemaVersion,
                SchemaStatus: "CorruptDatabase",
                Fts5Available: false,
                TechnicalNote: $"Database locale non leggibile: {ex.Message}");
        }
    }

    private StorageStatusResponse BuildStatus(
        int currentVersion,
        SqliteTextSearchBackend textSearchBackend,
        string? schemaStatus = null,
        string? schemaTechnicalNote = null)
    {
        string? technicalNote = textSearchBackend switch
        {
            SqliteTextSearchBackend.Fts5 => null,
            _ => FtsUnavailableNote
        };
        if (!string.IsNullOrWhiteSpace(schemaTechnicalNote))
        {
            technicalNote = string.IsNullOrWhiteSpace(technicalNote)
                ? schemaTechnicalNote
                : $"{schemaTechnicalNote} {technicalNote}";
        }

        return new StorageStatusResponse(
            descriptor.ProviderName,
            descriptor.Paths.DatabasePath,
            File.Exists(descriptor.Paths.DatabasePath),
            currentVersion,
            CurrentSchemaVersion,
            schemaStatus ?? (currentVersion == CurrentSchemaVersion ? "Current" : "ResetRequired"),
            textSearchBackend == SqliteTextSearchBackend.Fts5,
            technicalNote);
    }

    private static async Task<SchemaInspection> InspectSchemaAsync(
        SqliteConnection connection,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        bool hasCurrentSchema = await HasCurrentFreshSchemaAsync(connection, currentVersion, cancellationToken);
        if (hasCurrentSchema)
        {
            return new SchemaInspection("Current", null);
        }

        if (currentVersion == 2)
        {
            await MigrateFromV2ToV3Async(connection, cancellationToken);
            currentVersion = 3;
        }

        if (currentVersion == 3)
        {
            await MigrateFromV3ToV4Async(connection, cancellationToken);
            return new SchemaInspection("Current", null);
        }

        if (currentVersion > CurrentSchemaVersion)
        {
            return new SchemaInspection(
                "ResetRequired",
                "Il database locale usa una versione schema non supportata da questa app fresh.");
        }

        bool hasAnyUserTables = await HasAnyUserTablesAsync(connection, cancellationToken);
        if (currentVersion > 0 || hasAnyUserTables)
        {
            return new SchemaInspection(
                "ResetRequired",
                "Il database locale non corrisponde allo schema fresh corrente.");
        }

        return new SchemaInspection(
            "ResetRequired",
            "Il database locale e vuoto o non contiene lo schema fresh corrente.");
    }

    private static async Task MigrateFromV2ToV3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS document_graph_nodes (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    node_uid TEXT NOT NULL UNIQUE,
                    document_id INTEGER NULL,
                    chunk_id INTEGER NULL,
                    name TEXT NOT NULL,
                    type TEXT NOT NULL DEFAULT 'Concept',
                    description TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS document_graph_edges (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    edge_uid TEXT NOT NULL UNIQUE,
                    source_node_id INTEGER NOT NULL,
                    target_node_id INTEGER NOT NULL,
                    relation_type TEXT NOT NULL DEFAULT 'relates_to',
                    weight REAL NOT NULL DEFAULT 1.0,
                    chunk_id INTEGER NULL,
                    created_at_utc TEXT NOT NULL,
                    FOREIGN KEY (source_node_id) REFERENCES document_graph_nodes(id) ON DELETE CASCADE,
                    FOREIGN KEY (target_node_id) REFERENCES document_graph_nodes(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS agent_episodic_memories (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    goal TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    key_facts_json TEXT NOT NULL DEFAULT '[]',
                    qdrant_point_id TEXT NULL,
                    created_at_utc TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS agent_skills (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    skill_id TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    category TEXT NOT NULL,
                    pattern_description TEXT NOT NULL,
                    solution_template TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_graph_nodes_name ON document_graph_nodes(name);
                CREATE INDEX IF NOT EXISTS idx_graph_nodes_document ON document_graph_nodes(document_id);
                CREATE INDEX IF NOT EXISTS idx_graph_edges_source ON document_graph_edges(source_node_id);
                CREATE INDEX IF NOT EXISTS idx_graph_edges_target ON document_graph_edges(target_node_id);
                CREATE INDEX IF NOT EXISTS idx_episodic_memories_session ON agent_episodic_memories(session_id);
                CREATE INDEX IF NOT EXISTS idx_episodic_memories_created ON agent_episodic_memories(created_at_utc DESC);
                CREATE INDEX IF NOT EXISTS idx_agent_skills_category ON agent_skills(category);
                CREATE INDEX IF NOT EXISTS idx_agent_skills_created ON agent_skills(created_at_utc DESC);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await using SqliteCommand pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task MigrateFromV3ToV4Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_documents_original_path ON documents(original_path);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await using SqliteCommand pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private sealed record SchemaInspection(string Status, string? TechnicalNote);

    private static async Task<int> GetUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
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

        return SqliteTextSearchBackend.None;
    }

    private async Task<StorageStatusResponse> ResetAndCreateFreshSchemaAsync(
        string? resetReason,
        CancellationToken cancellationToken)
    {
        AppDataResetBackup backup = AppDataReset.ResetNow(descriptor.Paths);
        string note = string.IsNullOrWhiteSpace(resetReason)
            ? $"Database locale resettato per usare lo schema fresh corrente. Backup: {backup.BackupPath}"
            : $"{resetReason} Database locale resettato per usare lo schema fresh corrente. Backup: {backup.BackupPath}";
        foreach (string directory in descriptor.Paths.EnumerateRequiredDirectories())
        {
            LocalRuntimeDirectoryPreparer.EnsureDirectory(directory);
        }

        return await CreateFreshSchemaAsync(note, cancellationToken);
    }

    private async Task<StorageStatusResponse> CreateFreshSchemaAsync(
        string? schemaTechnicalNote,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
        await ApplyFreshSchemaAsync(connection, textSearchBackend, cancellationToken);
        return BuildStatus(CurrentSchemaVersion, textSearchBackend, schemaTechnicalNote: schemaTechnicalNote);
    }

    private static async Task<bool> HasCurrentFreshSchemaAsync(
        SqliteConnection connection,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        return currentVersion == CurrentSchemaVersion
            && await TableExistsAsync(connection, "documents", cancellationToken)
            && await TableExistsAsync(connection, "chunks", cancellationToken)
            && await TableExistsAsync(connection, "settings", cancellationToken)
            && await TableExistsAsync(connection, "document_graph_nodes", cancellationToken)
            && await TableExistsAsync(connection, "agent_episodic_memories", cancellationToken)
            && !await TableExistsAsync(connection, "schema_migrations", cancellationToken);
    }

    private static async Task<bool> HasAnyUserTablesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' LIMIT 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static async Task ApplyFreshSchemaAsync(
        SqliteConnection connection,
        SqliteTextSearchBackend textSearchBackend,
        CancellationToken cancellationToken)
    {
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = BuildFreshSchemaSql(textSearchBackend);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await using SqliteCommand pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = $"PRAGMA user_version = {CurrentSchemaVersion};";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
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
                parent_chunk_id INTEGER NULL,
                chunk_level TEXT NOT NULL DEFAULT 'Parent',
                section_heading TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE,
                FOREIGN KEY (document_page_id) REFERENCES document_pages(id) ON DELETE SET NULL,
                FOREIGN KEY (parent_chunk_id) REFERENCES chunks(id) ON DELETE CASCADE,
                UNIQUE (document_id, chunk_index)
            );

            CREATE TABLE chunk_vector_index_status (
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

            CREATE TABLE jobs (
                id TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ({{SqliteStatusConstraints.JobStatusPredicate}})),
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
                status TEXT NOT NULL CHECK (status IN ({{SqliteStatusConstraints.TranslationStatusPredicate}})),
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
                status TEXT NOT NULL DEFAULT 'Pending' CHECK (status IN ({{SqliteStatusConstraints.TranslationUnitStatusPredicate}})),
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

            CREATE TABLE generated_images (
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

            CREATE TABLE document_graph_nodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                node_uid TEXT NOT NULL UNIQUE,
                document_id INTEGER NULL,
                chunk_id INTEGER NULL,
                name TEXT NOT NULL,
                type TEXT NOT NULL DEFAULT 'Concept',
                description TEXT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (document_id) REFERENCES documents(id) ON DELETE CASCADE
            );

            CREATE TABLE document_graph_edges (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                edge_uid TEXT NOT NULL UNIQUE,
                source_node_id INTEGER NOT NULL,
                target_node_id INTEGER NOT NULL,
                relation_type TEXT NOT NULL DEFAULT 'relates_to',
                weight REAL NOT NULL DEFAULT 1.0,
                chunk_id INTEGER NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (source_node_id) REFERENCES document_graph_nodes(id) ON DELETE CASCADE,
                FOREIGN KEY (target_node_id) REFERENCES document_graph_nodes(id) ON DELETE CASCADE
            );

            CREATE TABLE agent_episodic_memories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                session_id TEXT NOT NULL,
                goal TEXT NOT NULL,
                summary TEXT NOT NULL,
                key_facts_json TEXT NOT NULL DEFAULT '[]',
                qdrant_point_id TEXT NULL,
                created_at_utc TEXT NOT NULL
            );

            CREATE UNIQUE INDEX ux_documents_sha256_not_null ON documents(sha256) WHERE sha256 IS NOT NULL;
            CREATE INDEX idx_documents_status_created ON documents(status, created_at_utc DESC);
            CREATE INDEX idx_document_pages_document ON document_pages(document_id);
            CREATE INDEX idx_document_pages_ocr ON document_pages(document_id, ocr_status, page_number);
            CREATE INDEX idx_chunks_document ON chunks(document_id);
            CREATE INDEX idx_chunks_page ON chunks(document_page_id);
            CREATE INDEX idx_chunks_document_ordinal ON chunks(document_id, chunk_index);
            CREATE INDEX idx_chunks_content_hash ON chunks(content_hash);
            CREATE INDEX idx_chunk_vector_index_status_chunk ON chunk_vector_index_status(chunk_id);
            CREATE INDEX idx_chunk_vector_index_status_model_chunk ON chunk_vector_index_status(model, chunk_id);
            CREATE INDEX idx_chunk_vector_index_status_content_hash ON chunk_vector_index_status(content_hash);
            CREATE INDEX idx_chunk_vector_index_status_collection ON chunk_vector_index_status(qdrant_collection);
            CREATE INDEX idx_jobs_status_priority ON jobs(status, priority DESC, created_at_utc);
            CREATE INDEX idx_jobs_pending_due ON jobs(status, next_attempt_at_utc, priority DESC, created_at_utc);
            CREATE INDEX idx_jobs_updated_at ON jobs(updated_at_utc);
            CREATE INDEX idx_chat_messages_conversation ON chat_messages(conversation_id, id);
            CREATE INDEX idx_translations_document ON translations(document_id, created_at_utc DESC);
            CREATE INDEX idx_translations_job ON translations(job_id);
            CREATE INDEX idx_translation_units_translation ON translation_units(translation_id, unit_index);
            CREATE INDEX idx_translation_units_status ON translation_units(translation_id, status, unit_index);
            CREATE INDEX idx_generated_images_created_at ON generated_images(created_at_utc DESC, id DESC);
            CREATE INDEX idx_ocr_cache_lookup
            ON ocr_cache(page_hash, engine_name, engine_version, language, preprocess_version);
            CREATE INDEX idx_graph_nodes_name ON document_graph_nodes(name);
            CREATE INDEX idx_graph_nodes_document ON document_graph_nodes(document_id);
            CREATE INDEX idx_graph_edges_source ON document_graph_edges(source_node_id);
            CREATE INDEX idx_graph_edges_target ON document_graph_edges(target_node_id);
            CREATE INDEX idx_episodic_memories_session ON agent_episodic_memories(session_id);
            CREATE INDEX idx_episodic_memories_created ON agent_episodic_memories(created_at_utc DESC);
            CREATE INDEX idx_documents_original_path ON documents(original_path);
            {{ftsSql}}

            PRAGMA user_version = 4;
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

    private enum SqliteTextSearchBackend
    {
        None,
        Fts5
    }
}
