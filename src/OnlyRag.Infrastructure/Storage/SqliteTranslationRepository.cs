using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteTranslationRepository : ITranslationRepository
{
    private const int MaxUnitCharacters = 6000;
    private static readonly Regex CellRegex = new(@"\[[A-Za-z]{1,4}\d{1,7}\]\s*[^|]+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteTranslationRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<TranslationSourceUnit>> BuildSourceUnitsAsync(
        long documentId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand documentCommand = connection.CreateCommand();
        documentCommand.CommandText = "SELECT LOWER(COALESCE(file_extension, '')) FROM documents WHERE id = $documentId;";
        documentCommand.AddParameter("$documentId", documentId);
        string extension = (await documentCommand.ExecuteScalarAsync(cancellationToken) as string) ?? string.Empty;

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, page_number, text_content
            FROM document_pages
            WHERE document_id = $documentId
              AND LENGTH(TRIM(COALESCE(text_content, ''))) > 0
            ORDER BY page_number ASC, id ASC;
            """;
        command.AddParameter("$documentId", documentId);

        List<TranslationSourceUnit> units = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            long pageId = reader.GetInt64(0);
            int pageNumber = reader.GetInt32(1);
            string text = reader.GetString(2);
            foreach ((string Kind, string Text) unit in SplitPageText(extension, text))
            {
                string normalized = unit.Text.Trim();
                if (normalized.Length == 0)
                {
                    continue;
                }

                units.Add(new TranslationSourceUnit(
                    units.Count,
                    unit.Kind,
                    pageId,
                    pageNumber,
                    normalized,
                    HashText(normalized),
                    CreateLayoutMetadata(extension, units.Count, unit.Kind, pageId, pageNumber)));
            }
        }

        return units;
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

    private static IEnumerable<(string Kind, string Text)> SplitPageText(string extension, string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (extension == ".xlsx")
        {
            foreach (Match match in CellRegex.Matches(normalized))
            {
                foreach (string segment in SplitLargeUnit(match.Value.Trim()))
                {
                    yield return ("table-cell", segment);
                }
            }

            yield break;
        }

        foreach (string paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (extension == ".pptx" && (paragraph.StartsWith("Textbox ", StringComparison.OrdinalIgnoreCase)
                || paragraph.StartsWith("Note ", StringComparison.OrdinalIgnoreCase)
                || paragraph.StartsWith("Slide ", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (string line in paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    foreach (string segment in SplitLargeUnit(line))
                    {
                        yield return ("textbox", segment);
                    }
                }

                continue;
            }

            if (paragraph.StartsWith("Riga ", StringComparison.OrdinalIgnoreCase)
                || paragraph.Contains("Cella ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string cell in paragraph.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    foreach (string segment in SplitLargeUnit(cell))
                    {
                        yield return ("table-cell", segment);
                    }
                }

                continue;
            }

            foreach (string segment in SplitLargeUnit(paragraph))
            {
                yield return ("paragraph", segment);
            }
        }
    }

    private static IEnumerable<string> SplitLargeUnit(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length <= MaxUnitCharacters)
        {
            yield return trimmed;
            yield break;
        }

        int start = 0;
        while (start < trimmed.Length)
        {
            int length = Math.Min(MaxUnitCharacters, trimmed.Length - start);
            int end = start + length;
            if (end < trimmed.Length)
            {
                int lineBreak = trimmed.LastIndexOf('\n', end - 1, length);
                if (lineBreak > start + 500)
                {
                    end = lineBreak;
                }
            }

            yield return trimmed[start..end].Trim();
            start = end;
        }
    }

    private static string HashText(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string CreateLayoutMetadata(
        string extension,
        int unitIndex,
        string unitKind,
        long pageId,
        int pageNumber)
    {
        TranslationLayoutMetadata metadata = new(
            extension,
            pageNumber,
            pageId,
            unitIndex,
            unitKind);
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

    private static string BuildTranslationSelect(string whereClause)
    {
        return
            $$"""
            SELECT t.id, t.document_id, COALESCE(d.original_file_name, 'Documento eliminato'),
                   t.source_language, t.target_language, t.model, t.status, t.job_id,
                   t.unit_count, t.completed_unit_count, t.last_error,
                   t.created_at_utc, t.updated_at_utc
            FROM translations AS t
            LEFT JOIN documents AS d ON d.id = t.document_id
            {{whereClause}};
            """;
    }

    private static string BuildUnitSelect(string whereClause)
    {
        return
            $$"""
            SELECT id, translation_id, unit_index, unit_kind, document_page_id, page_number,
                   source_text, source_hash, layout_metadata_json, machine_translated_text, translated_text, status,
                   manually_edited, validation_warnings, error, attempt_count, created_at_utc,
                   updated_at_utc
            FROM translation_units
            {{whereClause}};
            """;
    }

    private static StoredTranslation ReadTranslation(SqliteDataReader reader)
    {
        return new StoredTranslation(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            DateTimeOffset.Parse(reader.GetString(11)),
            DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static StoredTranslationUnit ReadUnit(SqliteDataReader reader)
    {
        return new StoredTranslationUnit(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? "{}" : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.GetString(11),
            reader.GetInt32(12) != 0,
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetInt32(15),
            DateTimeOffset.Parse(reader.GetString(16)),
            DateTimeOffset.Parse(reader.GetString(17)));
    }

    private sealed record TranslationLayoutMetadata(
        string SourceExtension,
        int PageNumber,
        long DocumentPageId,
        int UnitIndex,
        string UnitKind);
}
