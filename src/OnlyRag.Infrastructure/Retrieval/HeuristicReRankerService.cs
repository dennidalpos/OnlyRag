using System.Text.RegularExpressions;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class HeuristicReRankerService : IReRankerService
{
    private static readonly Regex WordRegex = new(@"\w+", RegexOptions.Compiled);

    public Task<IReadOnlyList<ReRankResult>> ReRankAsync(
        string query,
        IReadOnlyList<ReRankCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<ReRankResult>>([]);
        }

        HashSet<string> queryTerms = WordRegex.Matches(query.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length > 2)
            .ToHashSet();

        List<ReRankResult> results = [];
        foreach (ReRankCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double score = ComputeCrossScore(query, queryTerms, candidate.Content);
            results.Add(new ReRankResult(candidate.ChunkId, Math.Round(score, 4)));
        }

        IReadOnlyList<ReRankResult> sorted = results
            .OrderByDescending(r => r.Score)
            .ToList();

        return Task.FromResult(sorted);
    }

    private static double ComputeCrossScore(string query, HashSet<string> queryTerms, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0d;
        }

        string normalizedContent = content.ToLowerInvariant();
        if (normalizedContent.Contains(query.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            return 0.95d;
        }

        if (queryTerms.Count == 0)
        {
            return 0.5d;
        }

        int matchCount = queryTerms.Count(term => normalizedContent.Contains(term, StringComparison.OrdinalIgnoreCase));
        double termOverlapRatio = (double)matchCount / queryTerms.Count;

        // Position bonus: term appears in first 200 chars
        double positionBonus = queryTerms.Any(term =>
        {
            int idx = normalizedContent.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            return idx is >= 0 and <= 200;
        }) ? 0.15d : 0d;

        double finalScore = Math.Clamp(termOverlapRatio * 0.8d + positionBonus, 0.05d, 0.99d);
        return finalScore;
    }
}
