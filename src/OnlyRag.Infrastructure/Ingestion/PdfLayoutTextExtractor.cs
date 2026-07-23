using System.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace OnlyRag.Infrastructure.Ingestion;

public static class PdfLayoutTextExtractor
{
    public static string ExtractFormattedText(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        List<Word> words = page.GetWords().ToList();
        if (words.Count == 0)
        {
            return string.Empty;
        }

        double pageAverageFontSize = words.Average(w => w.BoundingBox.Height);
        if (pageAverageFontSize <= 0)
        {
            pageAverageFontSize = 10;
        }

        // Group words into lines based on Y baseline proximity
        List<ExtractedLine> lines = GroupWordsIntoLines(words);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder result = new();
        ExtractedLine? previousLine = null;

        for (int i = 0; i < lines.Count; i++)
        {
            ExtractedLine line = lines[i];
            if (string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            bool isHeading = line.AverageFontSize >= pageAverageFontSize * 1.25 || line.IsBoldHeading;
            bool isListItem = IsListMarker(line.Text);
            bool isTableLine = IsTableStructure(line, lines, i);

            if (previousLine != null)
            {
                double lineGap = previousLine.Bottom - line.Top; // In PDF coords, Y decreases downwards
                double threshold = Math.Max(previousLine.Height, line.Height) * 0.4;

                if (isHeading || isTableLine != IsTableStructure(previousLine, lines, i - 1) || lineGap > threshold || isListItem)
                {
                    result.AppendLine();
                }
            }

            if (isHeading)
            {
                string headerPrefix = line.AverageFontSize >= pageAverageFontSize * 1.5 ? "# " : "## ";
                string cleanText = line.Text.TrimStart('#', ' ');
                result.AppendLine($"{headerPrefix}{cleanText}");
            }
            else if (isTableLine)
            {
                result.AppendLine(FormatAsMarkdownTableRow(line));
            }
            else
            {
                result.AppendLine(line.Text);
            }

            previousLine = line;
        }

        return result.ToString().Trim();
    }

    private static List<ExtractedLine> GroupWordsIntoLines(List<Word> words)
    {
        // Sort words top to bottom (Y descending), left to right (X ascending)
        List<Word> sortedWords = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        List<List<Word>> lineGroups = [];

        foreach (Word word in sortedWords)
        {
            bool addedToLine = false;
            foreach (List<Word> currentLine in lineGroups)
            {
                Word lineReference = currentLine[0];
                double yDiff = Math.Abs(word.BoundingBox.Bottom - lineReference.BoundingBox.Bottom);
                double maxHeight = Math.Max(word.BoundingBox.Height, lineReference.BoundingBox.Height);

                if (yDiff <= maxHeight * 0.5)
                {
                    currentLine.Add(word);
                    addedToLine = true;
                    break;
                }
            }

            if (!addedToLine)
            {
                lineGroups.Add([word]);
            }
        }

        // Sort line groups top to bottom
        lineGroups = lineGroups
            .OrderByDescending(g => g.Average(w => w.BoundingBox.Bottom))
            .ToList();

        List<ExtractedLine> resultLines = [];
        foreach (List<Word> group in lineGroups)
        {
            List<Word> orderedGroupWords = group.OrderBy(w => w.BoundingBox.Left).ToList();

            StringBuilder lineBuilder = new();
            Word? prevWord = null;

            foreach (Word word in orderedGroupWords)
            {
                if (prevWord != null)
                {
                    double gap = word.BoundingBox.Left - prevWord.BoundingBox.Right;
                    if (gap > word.BoundingBox.Height * 0.25)
                    {
                        lineBuilder.Append(' ');
                    }
                    if (gap > word.BoundingBox.Height * 2.0)
                    {
                        // Extra spacing for potential tabular column separation
                        lineBuilder.Append("  ");
                    }
                }
                lineBuilder.Append(word.Text);
                prevWord = word;
            }

            string text = lineBuilder.ToString().Trim();
            if (text.Length > 0)
            {
                double avgFont = orderedGroupWords.Average(w => w.BoundingBox.Height);
                double top = orderedGroupWords.Max(w => w.BoundingBox.Top);
                double bottom = orderedGroupWords.Min(w => w.BoundingBox.Bottom);
                double height = Math.Max(1.0, top - bottom);
                bool isBold = orderedGroupWords.Any(w => w.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true);

                resultLines.Add(new ExtractedLine(text, avgFont, top, bottom, height, isBold, orderedGroupWords));
            }
        }

        return resultLines;
    }

    private static bool IsListMarker(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text.TrimStart();
        return trimmed.StartsWith("• ") || trimmed.StartsWith("- ") || trimmed.StartsWith("* ")
            || (trimmed.Length >= 3 && char.IsDigit(trimmed[0]) && (trimmed[1] == '.' || trimmed[1] == ')') && trimmed[2] == ' ');
    }

    private static bool IsTableStructure(ExtractedLine line, List<ExtractedLine> allLines, int index)
    {
        if (line.Words.Count < 2) return false;

        // Check wide internal gaps between words or alignment with adjacent lines
        int wideGaps = 0;
        for (int i = 0; i < line.Words.Count - 1; i++)
        {
            double gap = line.Words[i + 1].BoundingBox.Left - line.Words[i].BoundingBox.Right;
            if (gap > line.AverageFontSize * 1.8)
            {
                wideGaps++;
            }
        }

        if (wideGaps >= 1)
        {
            if (index > 0 && HasSimilarGaps(allLines[index - 1], line.AverageFontSize)) return true;
            if (index < allLines.Count - 1 && HasSimilarGaps(allLines[index + 1], line.AverageFontSize)) return true;
        }

        return false;
    }

    private static bool HasSimilarGaps(ExtractedLine line, double avgFontSize)
    {
        if (line.Words.Count < 2) return false;
        for (int i = 0; i < line.Words.Count - 1; i++)
        {
            double gap = line.Words[i + 1].BoundingBox.Left - line.Words[i].BoundingBox.Right;
            if (gap > avgFontSize * 1.5) return true;
        }
        return false;
    }

    private static string FormatAsMarkdownTableRow(ExtractedLine line)
    {
        List<string> columns = [];
        StringBuilder currentCol = new();

        for (int i = 0; i < line.Words.Count; i++)
        {
            Word word = line.Words[i];
            if (currentCol.Length > 0 && i > 0)
            {
                double gap = word.BoundingBox.Left - line.Words[i - 1].BoundingBox.Right;
                if (gap > line.AverageFontSize * 1.8)
                {
                    columns.Add(currentCol.ToString().Trim());
                    currentCol.Clear();
                }
                else
                {
                    currentCol.Append(' ');
                }
            }
            currentCol.Append(word.Text);
        }

        if (currentCol.Length > 0)
        {
            columns.Add(currentCol.ToString().Trim());
        }

        if (columns.Count <= 1)
        {
            return line.Text;
        }

        return "| " + string.Join(" | ", columns) + " |";
    }

    private sealed record ExtractedLine(
        string Text,
        double AverageFontSize,
        double Top,
        double Bottom,
        double Height,
        bool IsBoldHeading,
        IReadOnlyList<Word> Words);
}
