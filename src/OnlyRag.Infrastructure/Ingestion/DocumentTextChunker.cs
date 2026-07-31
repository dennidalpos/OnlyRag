using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class DocumentTextChunker
{
    private static readonly Regex TokenRegex = new(@"\S+", RegexOptions.Compiled);
    private static readonly Regex SentenceBoundaryRegex = new(@"(?<=[.!?;])\s+", RegexOptions.Compiled | RegexOptions.RightToLeft);

    private static int FindLastSentenceBoundary(string text)
    {
        Match match = SentenceBoundaryRegex.Match(text);
        return match.Success ? match.Index : -1;
    }

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

            string[] lines = paragraph.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int lineIndex = 0;
            while (lineIndex < lines.Length)
            {
                if (IsTableLine(lines[lineIndex]))
                {
                    List<string> tableBlockLines = [];
                    while (lineIndex < lines.Length && IsTableLine(lines[lineIndex]))
                    {
                        tableBlockLines.Add(lines[lineIndex]);
                        lineIndex++;
                    }

                    string tableText = string.Join("\n", tableBlockLines);
                    int tableTokens = EstimateTokenCount(tableText);
                    if (tableTokens > 0)
                    {
                        if (tableTokens <= DocumentIngestionOptions.MaximumChunkSizeTokens)
                        {
                            units.Add(new TextUnit(tableText, tableTokens));
                        }
                        else
                        {
                            // If single table block is huge, add as sub-line units
                            foreach (string tLine in tableBlockLines)
                            {
                                int tTokens = EstimateTokenCount(tLine);
                                if (tTokens > 0)
                                {
                                    units.Add(new TextUnit(tLine, tTokens));
                                }
                            }
                        }
                    }
                    continue;
                }

                string line = lines[lineIndex++];
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

                AddSentenceUnits(line, units);
            }
        }

        return units;
    }

    private static bool IsTableLine(string line)
    {
        string trimmed = line.Trim();
        return (trimmed.StartsWith('|') && trimmed.EndsWith('|') && trimmed.Length > 2)
            || trimmed.Contains("|---|", StringComparison.Ordinal)
            || (trimmed.Contains('|') && trimmed.Count(c => c == '|') >= 2);
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

    private static void AddSentenceUnits(string text, List<TextUnit> units)
    {
        // Split at sentence boundaries first
        string[] sentences = Regex.Split(text, @"(?<=[.!?])\s+(?=[A-Z\p{Lu}])");
        StringBuilder builder = new();
        int tokens = 0;

        foreach (string sentence in sentences)
        {
            int sentenceTokens = EstimateTokenCount(sentence);
            if (sentenceTokens == 0) continue;

            if (sentenceTokens > DocumentIngestionOptions.MaximumChunkSizeTokens)
            {
                if (tokens > 0)
                {
                    units.Add(new TextUnit(builder.ToString().Trim(), tokens));
                    builder.Clear();
                    tokens = 0;
                }
                AddWordUnits(sentence, units);
                continue;
            }

            // If adding this sentence would exceed min chunk, flush current
            if (tokens > 0 && tokens + sentenceTokens > DocumentIngestionOptions.MinimumChunkSizeTokens)
            {
                units.Add(new TextUnit(builder.ToString().Trim(), tokens));
                builder.Clear();
                tokens = 0;
            }

            if (builder.Length > 0) builder.Append(' ');
            builder.Append(sentence);
            tokens += sentenceTokens;
        }

        if (tokens > 0)
            units.Add(new TextUnit(builder.ToString().Trim(), tokens));
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
                int boundary = FindLastSentenceBoundary(unit.Text);
                if (boundary > 0)
                {
                    string partial = unit.Text[boundary..].TrimStart();
                    int partialTokens = EstimateTokenCount(partial);
                    if (partialTokens > 0 && tokens + partialTokens <= overlapTokens)
                    {
                        overlap.Insert(0, new TextUnit(partial, partialTokens));
                    }
                }
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
