using Microsoft.Data.Sqlite;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ocr;

public sealed class SqliteOcrCacheRepository : IOcrCacheRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteOcrCacheRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<OcrCacheEntry?> GetAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT cache_key, page_hash, engine_name, engine_version, language, preprocess_version,
                   text_content, boxes_json, confidence, created_at_utc, updated_at_utc
            FROM ocr_cache
            WHERE cache_key = $cacheKey;
            """;
        command.AddParameter("$cacheKey", cacheKey);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadEntry(reader)
            : null;
    }

    public async Task UpsertAsync(OcrCacheEntry entry, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ocr_cache (
                cache_key,
                page_hash,
                engine_name,
                engine_version,
                language,
                preprocess_version,
                text_content,
                boxes_json,
                confidence,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                $cacheKey,
                $pageHash,
                $engineName,
                $engineVersion,
                $language,
                $preprocessVersion,
                $text,
                $boxesJson,
                $confidence,
                $createdAtUtc,
                $updatedAtUtc
            )
            ON CONFLICT(cache_key) DO UPDATE SET
                text_content = excluded.text_content,
                boxes_json = excluded.boxes_json,
                confidence = excluded.confidence,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.AddParameter("$cacheKey", entry.CacheKey);
        command.AddParameter("$pageHash", entry.PageHash);
        command.AddParameter("$engineName", entry.EngineName);
        command.AddParameter("$engineVersion", entry.EngineVersion);
        command.AddParameter("$language", entry.Language);
        command.AddParameter("$preprocessVersion", entry.PreprocessVersion);
        command.AddParameter("$text", entry.Text);
        command.AddParameter("$boxesJson", entry.BoxesJson);
        command.AddParameter("$confidence", entry.Confidence);
        command.AddParameter("$createdAtUtc", entry.CreatedAtUtc.ToString("O"));
        command.AddParameter("$updatedAtUtc", entry.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static OcrCacheEntry ReadEntry(SqliteDataReader reader)
    {
        return new OcrCacheEntry(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            DateTimeOffset.Parse(reader.GetString(9)),
            DateTimeOffset.Parse(reader.GetString(10)));
    }
}
