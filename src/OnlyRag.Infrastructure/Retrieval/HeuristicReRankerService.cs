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

        string normalizedQuery = query.ToLowerInvariant();
        List<string> queryTokens = WordRegex.Matches(normalizedQuery)
            .Select(m => m.Value)
            .Where(w => w.Length > 2)
            .ToList();

        HashSet<string> queryTerms = queryTokens.ToHashSet();
        List<string> queryBigrams = GetBigrams(queryTokens);
        HashSet<string> queryBigramSet = queryBigrams.ToHashSet();

        // Compute batch-level IDF for query terms
        Dictionary<string, double> idf = [];
        int docCount = candidates.Count;
        List<List<string>> candidateTokensList = new(docCount);

        foreach (ReRankCandidate candidate in candidates)
        {
            List<string> tokens = WordRegex.Matches(candidate.Content.ToLowerInvariant())
                .Select(m => m.Value)
                .Where(w => w.Length > 2)
                .ToList();

            candidateTokensList.Add(tokens);

            HashSet<string> uniqueTokens = tokens.ToHashSet();
            foreach (string term in queryTerms)
            {
                if (uniqueTokens.Contains(term))
                {
                    idf[term] = idf.GetValueOrDefault(term) + 1;
                }
            }
        }

        foreach (string term in queryTerms)
        {
            double df = idf.GetValueOrDefault(term, 0);
            idf[term] = Math.Log((docCount + 1) / (df + 1)) + 1; // smoothed IDF
        }

        List<ReRankResult> results = [];
        for (int i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReRankCandidate candidate = candidates[i];
            double score = ComputeCrossScore(
                normalizedQuery,
                queryTerms,
                queryBigramSet,
                idf,
                candidate.Content,
                candidateTokensList[i]);

            results.Add(new ReRankResult(candidate.ChunkId, Math.Round(score, 4)));
        }

        IReadOnlyList<ReRankResult> sorted = results
            .OrderByDescending(r => r.Score)
            .ToList();

        return Task.FromResult(sorted);
    }

    private static List<string> GetBigrams(List<string> tokens)
    {
        List<string> bigrams = [];
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            bigrams.Add($"{tokens[i]} {tokens[i + 1]}");
        }
        return bigrams;
    }

    private static double ComputeCrossScore(
        string normalizedQuery,
        HashSet<string> queryTerms,
        HashSet<string> queryBigramSet,
        Dictionary<string, double> idf,
        string content,
        List<string> candidateTokens)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0d;
        }

        string normalizedContent = content.ToLowerInvariant();
        if (normalizedContent.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 0.95d;
        }

        if (queryTerms.Count == 0)
        {
            return 0.5d;
        }

        Dictionary<string, int> tf = [];
        foreach (string token in candidateTokens)
        {
            if (queryTerms.Contains(token))
            {
                tf[token] = tf.GetValueOrDefault(token) + 1;
            }
        }

        double idfSumMatched = 0d;
        double idfSumTotal = 0d;

        foreach (string term in queryTerms)
        {
            double termIdf = idf[term];
            idfSumTotal += termIdf;
            if (tf.ContainsKey(term))
            {
                idfSumMatched += termIdf;
            }
        }

        double unigramRatio = idfSumTotal > 0 ? idfSumMatched / idfSumTotal : 0d;

        List<string> candidateBigrams = GetBigrams(candidateTokens);
        int bigramMatches = 0;
        foreach (string bg in candidateBigrams)
        {
            if (queryBigramSet.Contains(bg))
            {
                bigramMatches++;
            }
        }

        double bigramRatio = queryBigramSet.Count > 0
            ? Math.Min((double)bigramMatches / queryBigramSet.Count, 1.0d)
            : 0d;

        int matchedTokens = tf.Values.Sum();
        double candidateCoverage = candidateTokens.Count > 0 ? (double)matchedTokens / candidateTokens.Count : 0d;

        double finalScore = Math.Clamp(unigramRatio * 0.5d + bigramRatio * 0.3d + candidateCoverage * 0.2d, 0.05d, 0.99d);
        return finalScore;
    }
}
