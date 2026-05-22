using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteMigrator
{
    public const int TargetSchemaVersion = 12;
    private const string InitialSchemaName = "012_fresh_local_storage";
    private const string BackupDirectoryName = "backups";
    private const string FtsUnavailableNote = "No SQLite FTS module is available in the active SQLite provider; keyword search is disabled until FTS5 or FTS4 is available.";

    private static readonly IReadOnlyList<SqliteSchemaMigration> Migrations =
    [
        new(9, "009_add_document_jobs_hashes_and_translation_layout", ApplySchemaVersion9Async),
        new(10, "010_add_fts4_keyword_search_fallback", ApplySchemaVersion10Async),
        new(11, "011_enforce_unique_document_hashes", ApplySchemaVersion11Async),
        new(12, "012_add_status_constraints", ApplySchemaVersion12Async)
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
