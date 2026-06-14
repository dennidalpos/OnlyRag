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
            foreach ((string Kind, string Text, string DisplayLabel) unit in SplitPageText(extension, text, pageNumber))
            {
                string normalized = unit.Text.Trim();
                if (normalized.Length == 0)
                {
                    continue;
                }

                units.Add(new TranslationSourceUnit(
                    units.Count,
                    unit.Kind,
                    unit.DisplayLabel,
                    pageId,
                    pageNumber,
                    normalized,
                    HashText(normalized),
                    CreateLayoutMetadata(extension, units.Count, unit.Kind, unit.DisplayLabel, pageId, pageNumber)));
            }
        }

        return units;
    }

    private static IEnumerable<(string Kind, string Text, string DisplayLabel)> SplitPageText(
        string extension,
        string text,
        int pageNumber)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (extension == ".xlsx")
        {
            string sheetName = ExtractNamedPrefix(normalized, "Foglio") ?? $"Foglio {pageNumber}";
            foreach (Match match in CellRegex.Matches(normalized))
            {
                string cellText = match.Value.Trim();
                string coordinate = ExtractBracketPrefix(cellText) ?? "Cella";
                foreach (string segment in SplitLargeUnit(cellText))
                {
                    yield return ("table-cell", segment, $"{sheetName} - {coordinate}");
                }
            }

            yield break;
        }

        if (extension == ".csv")
        {
            int rowNumber = 0;
            foreach (string line in normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                rowNumber++;
                int columnNumber = 0;
                foreach (string cell in SplitCsvLine(line))
                {
                    columnNumber++;
                    string trimmed = cell.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    foreach (string segment in SplitLargeUnit(trimmed))
                    {
                        yield return ("table-cell", segment, $"Riga {rowNumber} - Colonna {columnNumber}");
                    }
                }
            }

            yield break;
        }

        int paragraphNumber = 0;
        int lineNumber = 0;
        foreach (string paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (extension == ".pptx")
            {
                foreach (string line in paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    Match match = PresentationLineRegex.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }

                    string kind = match.Groups[1].Value.Equals("Note", StringComparison.OrdinalIgnoreCase)
                        ? "slide-note"
                        : "textbox";
                    string index = match.Groups[2].Value;
                    string label = $"Slide {pageNumber} - {match.Groups[1].Value} {index}";
                    foreach (string segment in SplitLargeUnit(line))
                    {
                        yield return (kind, segment, label);
                    }
                }

                continue;
            }

            if (paragraph.StartsWith("Titolo:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string segment in SplitLargeUnit(paragraph))
                {
                    yield return ("heading", segment, $"Sezione {pageNumber} - Titolo");
                }

                continue;
            }

            string tableText = paragraph.StartsWith("Tabella:", StringComparison.OrdinalIgnoreCase)
                ? paragraph["Tabella:".Length..].Trim()
                : paragraph;
            if (tableText.StartsWith("Riga ", StringComparison.OrdinalIgnoreCase)
                || tableText.Contains("Cella ", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string line in tableText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int cellNumber = 0;
                    foreach (string cell in line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        cellNumber++;
                        foreach (string segment in SplitLargeUnit(cell))
                        {
                            yield return ("table-cell", segment, $"Pagina {pageNumber} - Cella {cellNumber}");
                        }
                    }
                }

                continue;
            }

            if (IsOcrImageExtension(extension))
            {
                foreach (string line in paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    lineNumber++;
                    foreach (string segment in SplitLargeUnit(line))
                    {
                        yield return ("ocr-line", segment, $"Immagine - Riga {lineNumber}");
                    }
                }

                continue;
            }

            paragraphNumber++;
            string defaultLabel = extension == ".pdf"
                ? $"Pagina {pageNumber} - Paragrafo {paragraphNumber}"
                : $"Sezione {pageNumber} - Paragrafo {paragraphNumber}";
            foreach (string segment in SplitLargeUnit(paragraph))
            {
                yield return ("paragraph", segment, defaultLabel);
            }
        }
    }

    private static IEnumerable<string> SplitCsvLine(string line)
    {
        StringBuilder builder = new();
        bool inQuotes = false;
        for (int index = 0; index < line.Length; index++)
        {
            char current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                yield return builder.ToString();
                builder.Clear();
                continue;
            }

            builder.Append(current);
        }

        yield return builder.ToString();
    }

    private static string? ExtractNamedPrefix(string text, string prefix)
    {
        string firstLine = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        string marker = $"{prefix}:";
        return firstLine.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
            ? firstLine[marker.Length..].Trim()
            : null;
    }

    private static string? ExtractBracketPrefix(string text)
    {
        Match match = Regex.Match(text, @"^\[([A-Za-z]{1,4}\d{1,7})\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool IsOcrImageExtension(string extension)
    {
        return extension is ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp";
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
        string displayLabel,
        long pageId,
        int pageNumber)
    {
        TranslationLayoutMetadata metadata = new(
            extension,
            pageNumber,
            pageId,
            unitIndex,
            unitKind,
            displayLabel);
        return JsonSerializer.Serialize(metadata, JsonOptions);
    }

}
