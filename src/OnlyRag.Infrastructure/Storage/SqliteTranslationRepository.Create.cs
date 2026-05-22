using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository
{
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
}
