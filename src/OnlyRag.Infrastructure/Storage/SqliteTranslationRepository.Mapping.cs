using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository
{
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
        string UnitKind,
        string? DisplayLabel = null);

}
