using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteMigrator
{
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
            CREATE INDEX idx_ocr_cache_lookup
            ON ocr_cache(page_hash, engine_name, engine_version, language, preprocess_version);
            {{ftsSql}}

            INSERT INTO schema_migrations(version, name, applied_at_utc)
            VALUES (14, '{{InitialSchemaName}}', strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
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

}
