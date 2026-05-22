using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository
{
    public async Task SaveUnitSuccessAsync(
        long unitId,
        string translatedText,
        string? validationWarnings,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE translation_units
            SET machine_translated_text = $translatedText,
                translated_text = CASE
                    WHEN manually_edited = 1 THEN translated_text
                    ELSE $translatedText
                END,
                status = CASE
                    WHEN manually_edited = 1 THEN status
                    ELSE 'Completed'
                END,
                validation_warnings = $validationWarnings,
                error = NULL,
                attempt_count = attempt_count + 1,
                completed_at_utc = $now,
                updated_at_utc = $now
            WHERE id = $unitId;
            """;
        command.AddParameter("$unitId", unitId);
        command.AddParameter("$translatedText", translatedText);
        command.AddParameter("$validationWarnings", validationWarnings);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveUnitFailureAsync(
        long unitId,
        string error,
        CancellationToken cancellationToken = default)
    {
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE translation_units
            SET status = 'Failed',
                error = $error,
                attempt_count = attempt_count + 1,
                updated_at_utc = $now
            WHERE id = $unitId;
            """;
        command.AddParameter("$unitId", unitId);
        command.AddParameter("$error", error);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredTranslationUnit?> UpdateUnitTextAsync(
        long translationId,
        long unitId,
        string translatedText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedText);
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE translation_units
            SET translated_text = $translatedText,
                manually_edited = 1,
                status = 'Corrected',
                validation_warnings = NULL,
                error = NULL,
                updated_at_utc = $now
            WHERE translation_id = $translationId
              AND id = $unitId;
            """;
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$unitId", unitId);
        command.AddParameter("$translatedText", translatedText.Trim());
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await UpdateProgressForCorrectionAsync(connection, translationId, now, cancellationToken);
        return await GetUnitAsync(translationId, unitId, cancellationToken);
    }

    public async Task UpdateTranslationJobAsync(
        long translationId,
        string? jobId,
        string status,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        SqliteStatusConstraints.ValidateTranslationStatus(status);
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE translations
            SET job_id = $jobId,
                status = $status,
                last_error = $lastError,
                updated_at_utc = $now
            WHERE id = $translationId;
            """;
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$jobId", jobId);
        command.AddParameter("$status", status);
        command.AddParameter("$lastError", lastError);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RefreshProgressAsync(
        long translationId,
        string status,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        SqliteStatusConstraints.ValidateTranslationStatus(status);
        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE translations
            SET completed_unit_count = (
                    SELECT COUNT(*)
                    FROM translation_units
                    WHERE translation_id = $translationId
                      AND status IN ('Completed', 'Corrected')
                ),
                status = $status,
                last_error = $lastError,
                updated_at_utc = $now
            WHERE id = $translationId;
            """;
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$status", status);
        command.AddParameter("$lastError", lastError);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateProgressForCorrectionAsync(
        SqliteConnection connection,
        long translationId,
        string now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand progressCommand = connection.CreateCommand();
        progressCommand.CommandText =
            """
            UPDATE translations
            SET completed_unit_count = (
                    SELECT COUNT(*)
                    FROM translation_units
                    WHERE translation_id = $translationId
                      AND status IN ('Completed', 'Corrected')
                ),
                status = CASE
                    WHEN unit_count <= (
                        SELECT COUNT(*)
                        FROM translation_units
                        WHERE translation_id = $translationId
                          AND status IN ('Completed', 'Corrected')
                    )
                    THEN 'Completed'
                    ELSE status
                END,
                last_error = CASE
                    WHEN unit_count <= (
                        SELECT COUNT(*)
                        FROM translation_units
                        WHERE translation_id = $translationId
                          AND status IN ('Completed', 'Corrected')
                    )
                    THEN NULL
                    ELSE last_error
                END,
                updated_at_utc = $now
            WHERE id = $translationId;
            """;
        progressCommand.AddParameter("$translationId", translationId);
        progressCommand.AddParameter("$now", now);
        await progressCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}
