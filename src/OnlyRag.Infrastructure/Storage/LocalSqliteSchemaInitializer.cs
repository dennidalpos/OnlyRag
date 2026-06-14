using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class LocalSqliteSchemaInitializer
{
    public const int CurrentSchemaVersion = 1;

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
        Fts5
    }
}
