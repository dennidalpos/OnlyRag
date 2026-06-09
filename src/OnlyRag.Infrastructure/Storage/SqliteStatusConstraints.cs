using Microsoft.Data.Sqlite;
using OnlyRag.Worker;

namespace OnlyRag.Infrastructure.Storage;

internal static class SqliteStatusConstraints
{
    private static readonly string[] TranslationStatuses = ["Queued", "Running", "Completed", "Failed"];

    public static readonly string JobStatusPredicate = BuildSqlLiteralList(Enum.GetNames<JobStatus>());
    public static readonly string TranslationStatusPredicate = BuildSqlLiteralList(TranslationStatuses);
    public static readonly string TranslationUnitStatusPredicate = BuildSqlLiteralList(["Pending", "Completed", "Failed", "Corrected"]);

    public static void ValidateTranslationStatus(string status)
    {
        if (!IsAllowed(status, TranslationStatuses))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Translation status is not supported.");
        }
    }

    public static string BuildJobStatusInPredicate(IReadOnlyCollection<JobStatus> statuses)
    {
        return $"status IN ({BuildJobStatusList(statuses)})";
    }

    public static string BuildJobStatusNotInPredicate(IReadOnlyCollection<JobStatus> statuses)
    {
        return $"status NOT IN ({BuildJobStatusList(statuses)})";
    }

    public static string BuildJobStatusEqualsPredicate(JobStatus status)
    {
        return $"status = {FormatJobStatusLiteral(status)}";
    }

    private static string BuildJobStatusList(IReadOnlyCollection<JobStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        if (statuses.Count == 0)
        {
            throw new ArgumentException("At least one job status is required.", nameof(statuses));
        }

        return string.Join(", ", statuses.Select(FormatJobStatusLiteral));
    }

    private static string BuildSqlLiteralList(IEnumerable<string> statuses)
    {
        return string.Join(", ", statuses.Select(status => $"'{status}'"));
    }

    private static string FormatJobStatusLiteral(JobStatus status)
    {
        return $"'{status}'";
    }

    public static async Task ValidateExistingStatusesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        List<string> invalidStatusGroups = [];
        await AddInvalidStatusesAsync(connection, transaction, invalidStatusGroups, "jobs", "status", JobStatusPredicate, cancellationToken);
        await AddInvalidStatusesAsync(connection, transaction, invalidStatusGroups, "translations", "status", TranslationStatusPredicate, cancellationToken);
        await AddInvalidStatusesAsync(connection, transaction, invalidStatusGroups, "translation_units", "status", TranslationUnitStatusPredicate, cancellationToken);

        if (invalidStatusGroups.Count > 0)
        {
            throw new InvalidOperationException(
                "Database SQLite contiene valori status non validi: "
                + string.Join("; ", invalidStatusGroups));
        }
    }

    public static async Task CreateValidationTriggersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await CreateTableStatusTriggersAsync(connection, transaction, "jobs", "status", JobStatusPredicate, cancellationToken);
        await CreateTableStatusTriggersAsync(connection, transaction, "translations", "status", TranslationStatusPredicate, cancellationToken);
        await CreateTableStatusTriggersAsync(connection, transaction, "translation_units", "status", TranslationUnitStatusPredicate, cancellationToken);
    }

    private static bool IsAllowed(string status, IReadOnlyCollection<string> allowedStatuses)
    {
        return allowedStatuses.Contains(status, StringComparer.Ordinal);
    }

    private static async Task AddInvalidStatusesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        List<string> invalidStatusGroups,
        string tableName,
        string columnName,
        string allowedStatuses,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, tableName, cancellationToken))
        {
            return;
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT {columnName}, COUNT(*) AS invalid_count
            FROM {tableName}
            WHERE {columnName} NOT IN ({allowedStatuses})
            GROUP BY {columnName}
            ORDER BY invalid_count DESC, {columnName} ASC
            LIMIT 5;
            """;

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string status = reader.GetString(0);
            long count = reader.GetInt64(1);
            invalidStatusGroups.Add($"{tableName}.{columnName}='{status}' ({count} rows)");
        }
    }

    private static async Task CreateTableStatusTriggersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string allowedStatuses,
        CancellationToken cancellationToken)
    {
        if (!await TableExistsAsync(connection, transaction, tableName, cancellationToken))
        {
            return;
        }

        await ExecuteAsync(
            connection,
            transaction,
            $"""
            CREATE TRIGGER IF NOT EXISTS trg_{tableName}_{columnName}_validate_insert
            BEFORE INSERT ON {tableName}
            WHEN NEW.{columnName} NOT IN ({allowedStatuses})
            BEGIN
                SELECT RAISE(ABORT, 'Invalid {tableName}.{columnName}');
            END;
            """,
            cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            $"""
            CREATE TRIGGER IF NOT EXISTS trg_{tableName}_{columnName}_validate_update
            BEFORE UPDATE OF {columnName} ON {tableName}
            WHEN NEW.{columnName} NOT IN ({allowedStatuses})
            BEGIN
                SELECT RAISE(ABORT, 'Invalid {tableName}.{columnName}');
            END;
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
}
