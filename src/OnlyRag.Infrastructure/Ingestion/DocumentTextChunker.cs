using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class DocumentTextChunker
{
    private static readonly Regex TokenRegex = new(@"\S+", RegexOptions.Compiled);

    public IReadOnlyList<IngestedDocumentChunk> CreateChunks(
        string text,
        int pageStart,
        int pageEnd,
        int firstOrdinal,
        DocumentIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        List<TextUnit> units = SplitIntoUnits(text);
        if (units.Count == 0)
        {
            return [];
        }

        List<IngestedDocumentChunk> chunks = [];
        List<TextUnit> current = [];
        int currentTokens = 0;
        int ordinal = firstOrdinal;

        foreach (TextUnit unit in units)
        {
            if (current.Count > 0 && currentTokens + unit.TokenCount > options.ChunkSizeTokens)
            {
                chunks.Add(CreateChunk(current, pageStart, pageEnd, ordinal++));
                current = BuildOverlap(current, options.OverlapTokens);
                currentTokens = current.Sum(item => item.TokenCount);
            }

            current.Add(unit);
            currentTokens += unit.TokenCount;
        }

        if (current.Count > 0)
        {
            chunks.Add(CreateChunk(current, pageStart, pageEnd, ordinal));
        }

        return chunks;
    }

    public static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return TokenRegex.Count(text);
    }

    private static List<TextUnit> SplitIntoUnits(string text)
    {
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        List<TextUnit> units = [];
        foreach (string paragraph in normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int paragraphTokens = EstimateTokenCount(paragraph);
            if (paragraphTokens == 0)
            {
                continue;
            }

            if (paragraphTokens <= DocumentIngestionOptions.MaximumChunkSizeTokens)
            {
                units.Add(new TextUnit(paragraph, paragraphTokens));
                continue;
            }

            foreach (string line in paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int lineTokens = EstimateTokenCount(line);
                if (lineTokens == 0)
                {
                    continue;
                }

                if (lineTokens <= DocumentIngestionOptions.MaximumChunkSizeTokens)
                {
                    units.Add(new TextUnit(line, lineTokens));
                    continue;
                }

                AddWordUnits(line, units);
            }
        }

        return units;
    }

    private static void AddWordUnits(string text, List<TextUnit> units)
    {
        StringBuilder builder = new();
        int tokens = 0;

        foreach (Match match in TokenRegex.Matches(text))
        {
            if (tokens >= DocumentIngestionOptions.MinimumChunkSizeTokens)
            {
                units.Add(new TextUnit(builder.ToString().Trim(), tokens));
                builder.Clear();
                tokens = 0;
            }

            builder.Append(match.Value).Append(' ');
            tokens++;
        }

        if (tokens > 0)
        {
            units.Add(new TextUnit(builder.ToString().Trim(), tokens));
        }
    }

    private static List<TextUnit> BuildOverlap(IReadOnlyList<TextUnit> units, int overlapTokens)
    {
        if (overlapTokens <= 0)
        {
            return [];
        }

        List<TextUnit> overlap = [];
        int tokens = 0;
        for (int index = units.Count - 1; index >= 0; index--)
        {
            TextUnit unit = units[index];
            if (overlap.Count > 0 && tokens + unit.TokenCount > overlapTokens)
            {
                break;
            }

            overlap.Insert(0, unit);
            tokens += unit.TokenCount;
        }

        return overlap;
    }

    private static IngestedDocumentChunk CreateChunk(
        IReadOnlyList<TextUnit> units,
        int pageStart,
        int pageEnd,
        int ordinal)
    {
        string text = string.Join("\n\n", units.Select(unit => unit.Text)).Trim();
        int tokenCount = units.Sum(unit => unit.TokenCount);
        string contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        return new IngestedDocumentChunk(pageStart, pageEnd, ordinal, text, tokenCount, contentHash);
    }

    private sealed record TextUnit(string Text, int TokenCount);
}
