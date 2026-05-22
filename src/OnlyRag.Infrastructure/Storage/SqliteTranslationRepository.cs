using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository : ITranslationRepository
{
    private const int MaxUnitCharacters = 6000;
    private static readonly Regex CellRegex = new(@"\[[A-Za-z]{1,4}\d{1,7}\]\s*[^|]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteTranslationRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }


    public async Task<StoredTranslation> CreateAsync(
        long documentId,
        string targetLanguage,
        string model,
        string? jobId,
        IReadOnlyList<TranslationSourceUnit> units,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (units.Count == 0)
        {
            throw new InvalidOperationException("Il documento non contiene unita testuali da tradurre.");
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        long translationId;

        await using (SqliteCommand insertTranslation = connection.CreateCommand())
        {
            insertTranslation.Transaction = transaction;
            insertTranslation.CommandText =
                """
                INSERT INTO translations (
                    document_id,
                    source_language,
                    target_language,
                    model,
                    status,
                    job_id,
                    unit_count,
                    completed_unit_count,
                    last_error,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    $documentId,
                    'auto',
                    $targetLanguage,
                    $model,
                    'Queued',
                    $jobId,
                    $unitCount,
                    0,
                    NULL,
                    $now,
                    $now
                );
                SELECT last_insert_rowid();
                """;
            insertTranslation.AddParameter("$documentId", documentId);
            insertTranslation.AddParameter("$targetLanguage", targetLanguage);
            insertTranslation.AddParameter("$model", model);
            insertTranslation.AddParameter("$jobId", jobId);
            insertTranslation.AddParameter("$unitCount", units.Count);
            insertTranslation.AddParameter("$now", now);
            translationId = Convert.ToInt64(await insertTranslation.ExecuteScalarAsync(cancellationToken));
        }

        foreach (TranslationSourceUnit unit in units)
        {
            await using SqliteCommand insertUnit = connection.CreateCommand();
            insertUnit.Transaction = transaction;
            insertUnit.CommandText =
                """
                INSERT INTO translation_units (
                    translation_id,
                    document_page_id,
                    unit_index,
                    unit_kind,
                    page_number,
                    source_text,
                    source_hash,
                    layout_metadata_json,
                    machine_translated_text,
                    translated_text,
                    manually_edited,
                    status,
                    validation_warnings,
                    error,
                    attempt_count,
                    created_at_utc,
                    updated_at_utc
                )
                VALUES (
                    $translationId,
                    $documentPageId,
                    $unitIndex,
                    $unitKind,
                    $pageNumber,
                    $sourceText,
                    $sourceHash,
                    $layoutMetadataJson,
                    NULL,
                    NULL,
                    0,
                    'Pending',
                    NULL,
                    NULL,
                    0,
                    $now,
                    $now
                );
                """;
            insertUnit.AddParameter("$translationId", translationId);
            insertUnit.AddParameter("$documentPageId", unit.DocumentPageId);
            insertUnit.AddParameter("$unitIndex", unit.UnitIndex);
            insertUnit.AddParameter("$unitKind", unit.UnitKind);
            insertUnit.AddParameter("$pageNumber", unit.PageNumber);
            insertUnit.AddParameter("$sourceText", unit.SourceText);
            insertUnit.AddParameter("$sourceHash", unit.SourceHash);
            insertUnit.AddParameter("$layoutMetadataJson", unit.LayoutMetadataJson);
            insertUnit.AddParameter("$now", now);
            await insertUnit.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(translationId, cancellationToken))!;
    }

    public async Task<StoredTranslation?> GetAsync(long translationId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildTranslationSelect("WHERE t.id = $translationId");
        command.AddParameter("$translationId", translationId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTranslation(reader) : null;
    }

    public async Task<IReadOnlyList<StoredTranslation>> ListByDocumentAsync(
        long documentId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildTranslationSelect("WHERE t.document_id = $documentId ORDER BY t.created_at_utc DESC, t.id DESC");
        command.AddParameter("$documentId", documentId);

        List<StoredTranslation> translations = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            translations.Add(ReadTranslation(reader));
        }

        return translations;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsAsync(
        long translationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect("WHERE translation_id = $translationId ORDER BY unit_index ASC");
        command.AddParameter("$translationId", translationId);

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsPreviewAsync(
        long translationId,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
            ORDER BY unit_index ASC
            LIMIT $take
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$take", Math.Clamp(take, 1, 200));

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<IReadOnlyList<int>> ListUnitPagesAsync(
        long translationId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT COALESCE(page_number, unit_index + 1)
            FROM translation_units
            WHERE translation_id = $translationId
            ORDER BY COALESCE(page_number, unit_index + 1) ASC;
            """;
        command.AddParameter("$translationId", translationId);

        List<int> pages = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pages.Add(reader.GetInt32(0));
        }

        return pages;
    }

    public async Task<IReadOnlyList<StoredTranslationUnit>> ListUnitsByPageAsync(
        long translationId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
              AND COALESCE(page_number, unit_index + 1) = $pageNumber
            ORDER BY unit_index ASC
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$pageNumber", pageNumber);

        List<StoredTranslationUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            units.Add(ReadUnit(reader));
        }

        return units;
    }

    public async Task<StoredTranslationUnit?> GetUnitAsync(
        long translationId,
        long unitId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect("WHERE translation_id = $translationId AND id = $unitId");
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$unitId", unitId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUnit(reader) : null;
    }

    public async Task<StoredTranslationUnit?> GetNextPendingUnitAsync(
        long translationId,
        int afterUnitIndex,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = BuildUnitSelect(
            """
            WHERE translation_id = $translationId
              AND unit_index >= $afterUnitIndex
              AND status IN ('Pending', 'Failed')
            ORDER BY unit_index ASC
            LIMIT 1
            """);
        command.AddParameter("$translationId", translationId);
        command.AddParameter("$afterUnitIndex", Math.Max(0, afterUnitIndex));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUnit(reader) : null;
    }

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


}
