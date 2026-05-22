using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed partial class SqliteTranslationRepository
{
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

}
