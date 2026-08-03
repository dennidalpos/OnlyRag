using UglyToad.PdfPig.Content;

namespace OnlyRag.Infrastructure.Ingestion;

public static class LayoutSpatialParser
{
    public sealed record TextBlock(
        string Text,
        double Left,
        double Right,
        double Top,
        double Bottom,
        double AverageFontSize);

    public static IReadOnlyList<TextBlock> ReorderMultiColumnBlocks(IReadOnlyList<Word> words, double pageWidth)
    {
        if (words == null || words.Count == 0) return [];

        // Detect if there are multiple columns (e.g. 2-column layout)
        double midPoint = pageWidth / 2.0;
        var leftColumnWords = words.Where(w => w.BoundingBox.Right <= midPoint + (pageWidth * 0.05)).ToList();
        var rightColumnWords = words.Where(w => w.BoundingBox.Left >= midPoint - (pageWidth * 0.05)).ToList();

        // If both columns contain substantial word count (e.g., > 25% of total words each) and low overlap, sort by column
        if (words.Count >= 20 &&
            leftColumnWords.Count >= words.Count * 0.25 &&
            rightColumnWords.Count >= words.Count * 0.25 &&
            (leftColumnWords.Count + rightColumnWords.Count) >= words.Count * 0.85)
        {
            var orderedLeft = SortColumnWordsTopToBottom(leftColumnWords);
            var orderedRight = SortColumnWordsTopToBottom(rightColumnWords);
            return [.. orderedLeft, .. orderedRight];
        }

        // Single column fallback
        return SortColumnWordsTopToBottom(words);
    }

    private static List<TextBlock> SortColumnWordsTopToBottom(IReadOnlyList<Word> words)
    {
        var sortedLines = words
            .GroupBy(w => Math.Round(w.BoundingBox.Bottom / 3.0) * 3.0)
            .OrderByDescending(g => g.Key)
            .ToList();

        List<TextBlock> blocks = [];
        foreach (var lineGroup in sortedLines)
        {
            var lineWords = lineGroup.OrderBy(w => w.BoundingBox.Left).ToList();
            string text = string.Join(" ", lineWords.Select(w => w.Text));
            double left = lineWords.Min(w => w.BoundingBox.Left);
            double right = lineWords.Max(w => w.BoundingBox.Right);
            double top = lineWords.Max(w => w.BoundingBox.Top);
            double bottom = lineWords.Min(w => w.BoundingBox.Bottom);
            double avgFont = lineWords.Average(w => w.BoundingBox.Height);

            blocks.Add(new TextBlock(text, left, right, top, bottom, avgFont));
        }

        return blocks;
    }
}
