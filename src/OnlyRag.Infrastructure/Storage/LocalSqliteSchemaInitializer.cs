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
        if (File.Exists(descriptor.Paths.DatabasePath))
        {
            await using SqliteConnection existingConnection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            SqliteTextSearchBackend existingTextSearchBackend = await DetectTextSearchBackendAsync(existingConnection, cancellationToken);
            int existingVersion = await GetUserVersionAsync(existingConnection, cancellationToken);
            bool isCurrent = existingVersion == CurrentSchemaVersion
                && !await TableExistsAsync(existingConnection, "schema_migrations", cancellationToken)
                && await TableExistsAsync(existingConnection, "documents", cancellationToken);

            if (isCurrent)
            {
                return BuildStatus(existingVersion, existingTextSearchBackend);
            }
        }

        if (Directory.Exists(descriptor.Paths.DataRoot))
        {
            SqliteConnection.ClearAllPools();
            AppDataReset.ResetNow(descriptor.Paths);
            foreach (string directory in descriptor.Paths.EnumerateRequiredDirectories())
            {
                LocalRuntimeDirectoryPreparer.EnsureDirectory(directory);
            }
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
        await ApplyFreshSchemaAsync(connection, textSearchBackend, cancellationToken);
        return BuildStatus(CurrentSchemaVersion, textSearchBackend);
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
                MigrationStatus: "NotInitialized",
                Fts5Available: false,
                TechnicalNote: null);
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        SqliteTextSearchBackend textSearchBackend = await DetectTextSearchBackendAsync(connection, cancellationToken);
        int currentVersion = await GetUserVersionAsync(connection, cancellationToken);
        bool hasSchemaMigrationTable = await TableExistsAsync(connection, "schema_migrations", cancellationToken);
        bool hasCurrentSchema = currentVersion == CurrentSchemaVersion
            && !hasSchemaMigrationTable
            && await TableExistsAsync(connection, "documents", cancellationToken);

        return hasCurrentSchema
            ? BuildStatus(currentVersion, textSearchBackend)
            : new StorageStatusResponse(
                descriptor.ProviderName,
                descriptor.Paths.DatabasePath,
                DatabaseExists: true,
                CurrentSchemaVersion: currentVersion,
                TargetSchemaVersion: CurrentSchemaVersion,
                MigrationStatus: "ResetRequired",
                Fts5Available: textSearchBackend == SqliteTextSearchBackend.Fts5,
                TechnicalNote: "Lo schema locale non appartiene alla app fresh corrente e verra ricreato all'avvio.");
    }

    private StorageStatusResponse BuildStatus(int currentVersion, SqliteTextSearchBackend textSearchBackend)
    {
        string? technicalNote = textSearchBackend switch
        {
            SqliteTextSearchBackend.Fts5 => null,
            SqliteTextSearchBackend.Fts4 => "SQLite FTS5 is unavailable; keyword search uses the indexed SQLite FTS4 fallback.",
            _ => FtsUnavailableNote
        };

        return new StorageStatusResponse(
            descriptor.ProviderName,
            descriptor.Paths.DatabasePath,
            File.Exists(descriptor.Paths.DatabasePath),
            currentVersion,
            CurrentSchemaVersion,
            currentVersion == CurrentSchemaVersion ? "Current" : "ResetRequired",
            textSearchBackend == SqliteTextSearchBackend.Fts5,
            technicalNote);
    }

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
