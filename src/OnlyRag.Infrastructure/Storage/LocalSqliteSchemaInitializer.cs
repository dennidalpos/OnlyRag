using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteSchemaInitializer
{
    public const int CurrentSchemaVersion = 1;

    private const string FtsUnavailableNote = "No SQLite FTS module is available in the active SQLite provider; keyword search is disabled until FTS5 or FTS4 is available.";

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
            await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
            await ApplyFreshSchemaAsync(connection, textSearchBackend, cancellationToken);
            return BuildStatus(CurrentSchemaVersion, textSearchBackend);
        }

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

            if (inspection.LegacySchemaVersion is { } legacySchemaVersion)
            {
                SchemaMigrationResult migrationResult = await TryMigrateLegacySchemaAsync(
                    existingConnection,
                    legacySchemaVersion,
                    existingTextSearchBackend,
                    cancellationToken);
                if (migrationResult.Migrated)
                {
                    return BuildStatus(CurrentSchemaVersion, existingTextSearchBackend);
                }

                return BuildStatus(
                    existingVersion,
                    existingTextSearchBackend,
                    "MigrationRequired",
                    migrationResult.TechnicalNote);
            }

            return BuildStatus(
                existingVersion,
                existingTextSearchBackend,
                inspection.Status,
                inspection.TechnicalNote);
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
            SqliteTextSearchBackend.Fts4 => "SQLite FTS5 is unavailable; keyword search uses the indexed SQLite FTS4 fallback.",
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
            schemaStatus ?? (currentVersion == CurrentSchemaVersion ? "Current" : "MigrationRequired"),
            textSearchBackend == SqliteTextSearchBackend.Fts5,
            technicalNote);
    }

    private static async Task<SchemaInspection> InspectSchemaAsync(
        SqliteConnection connection,
        int currentVersion,
        CancellationToken cancellationToken)
    {
        bool hasDocuments = await TableExistsAsync(connection, "documents", cancellationToken);
        bool hasSchemaMigrations = await TableExistsAsync(connection, "schema_migrations", cancellationToken);
        if (currentVersion == CurrentSchemaVersion && hasDocuments && !hasSchemaMigrations)
        {
            return new SchemaInspection("Current", null, null);
        }

        if (currentVersion > CurrentSchemaVersion)
        {
            return new SchemaInspection(
                "UnsupportedSchema",
                "Il database locale usa una versione schema piu recente; aggiorna l'app o ripristina da backup. Nessun dato e stato eliminato.",
                null);
        }

        if (hasSchemaMigrations)
        {
            int? legacySchemaVersion = await GetLegacySchemaVersionAsync(connection, cancellationToken);
            if (legacySchemaVersion is >= LegacySchemaMinimumSupportedVersion and <= LegacySchemaMaximumSupportedVersion)
            {
                return new SchemaInspection(
                    "MigrationRequired",
                    $"Il database locale usa lo schema legacy {legacySchemaVersion} e verra migrato allo schema corrente.",
                    legacySchemaVersion);
            }

            return new SchemaInspection(
                "UnsupportedSchema",
                "Il database locale usa metadati schema legacy non supportati. Nessun dato e stato eliminato automaticamente.",
                null);
        }

        if (currentVersion > 0 || hasDocuments)
        {
            return new SchemaInspection(
                "MigrationRequired",
                "Il database locale richiede una migrazione esplicita prima dell'uso. Nessun dato e stato eliminato automaticamente.",
                null);
        }

        return new SchemaInspection(
            "UnsupportedSchema",
            "Il database locale non contiene metadati schema riconosciuti. Nessun dato e stato eliminato automaticamente.",
            null);
    }

    private sealed record SchemaInspection(string Status, string? TechnicalNote, int? LegacySchemaVersion);

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
        try
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = BuildFreshSchemaSql(textSearchBackend);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private enum SqliteTextSearchBackend
    {
        None,
        Fts4,
        Fts5
    }
}
