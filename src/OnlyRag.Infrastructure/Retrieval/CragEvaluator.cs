using System.Collections.Generic;
using System.Linq;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public enum CragAction
{
    Accept,
    Reformulate,
    Abstain
}

public sealed record CragDecision(
    CragAction Action,
    double HighestScore,
    string SummaryNotice,
    IReadOnlyList<string>? ReformulatedQueries);

public sealed class CragDecisionEngine
{
    public CragDecision Evaluate(
        IReadOnlyList<DocumentSearchResult> results,
        string originalQuery,
        double threshold = 0.30,
        double lowThreshold = 0.15)
    {
        if (results.Count == 0)
        {
            return new CragDecision(CragAction.Abstain, 0d, "No candidates retrieved.", null);
        }

        double maxScore = results.Max(r => r.ReRankScore ?? r.Score);

        if (maxScore >= threshold)
        {
            return new CragDecision(
                CragAction.Accept,
                maxScore,
                $"High RAG confidence ({maxScore:F2}).",
                null);
        }

        if (maxScore >= lowThreshold)
        {
            var reformulations = GenerateReformulations(originalQuery);
            return new CragDecision(
                CragAction.Reformulate,
                maxScore,
                $"Low RAG confidence ({maxScore:F2} < {threshold:F2}). Attempting query reformulation.",
                reformulations);
        }

        return new CragDecision(
            CragAction.Abstain,
            maxScore,
            $"Insufficient RAG confidence ({maxScore:F2} < {lowThreshold:F2}).",
            null);
    }

    private static readonly char[] QuerySplitChars = [' ', '?', '.', ','];

    public IReadOnlyList<string> GenerateReformulations(string originalQuery)
    {
        var words = originalQuery.Split(QuerySplitChars, System.StringSplitOptions.RemoveEmptyEntries);
        var keyTerms = words.Where(w => w.Length > 3).ToList();

        if (keyTerms.Count == 0)
        {
            return [originalQuery + " explained", "What is " + originalQuery + "?"];
        }

        string rearranged = string.Join(" ", keyTerms.OrderByDescending(x => x.Length));
        string withFraming = "What is known about " + string.Join(" ", keyTerms) + "?";

        return [rearranged, withFraming];
    }
}
