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

/// <summary>
/// Self-Corrective RAG (CRAG) decision engine.
/// Evaluates retrieval confidence and generates LLM-powered query reformulations
/// when confidence is insufficient, replacing the previous naive string manipulation.
/// </summary>
public sealed class CragDecisionEngine
{
    private readonly ILlmQueryExpander? llmExpander;

    public CragDecisionEngine(ILlmQueryExpander? llmExpander = null)
    {
        this.llmExpander = llmExpander;
    }

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

    /// <summary>
    /// Async variant that uses LLM-powered reformulation when available.
    /// Falls back to heuristic reformulation when no LLM expander is configured.
    /// </summary>
    public async Task<CragDecision> EvaluateAsync(
        IReadOnlyList<DocumentSearchResult> results,
        string originalQuery,
        double threshold = 0.30,
        double lowThreshold = 0.15,
        CancellationToken cancellationToken = default)
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
            IReadOnlyList<string> reformulations = await GenerateReformulationsAsync(
                originalQuery, results, maxScore, cancellationToken);
            return new CragDecision(
                CragAction.Reformulate,
                maxScore,
                $"Low RAG confidence ({maxScore:F2} < {threshold:F2}). Attempting LLM-powered query reformulation.",
                reformulations);
        }

        return new CragDecision(
            CragAction.Abstain,
            maxScore,
            $"Insufficient RAG confidence ({maxScore:F2} < {lowThreshold:F2}).",
            null);
    }

    /// <summary>
    /// LLM-powered reformulation: asks the LLM to analyze why retrieval
    /// failed and generate better search queries.
    /// Falls back to heuristic reformulation if LLM is unavailable.
    /// </summary>
    private async Task<IReadOnlyList<string>> GenerateReformulationsAsync(
        string originalQuery,
        IReadOnlyList<DocumentSearchResult> topResults,
        double maxScore,
        CancellationToken cancellationToken)
    {
        if (llmExpander is null)
        {
            return GenerateReformulations(originalQuery);
        }

        try
        {
            string topSnippets = string.Join("\n",
                topResults
                    .Take(3)
                    .Select(r => $"- (score {r.Score:F2}) {Truncate(r.Snippet, 150)}"));

            string prompt = $$"""
                The following search query returned low-confidence results (best score: {{maxScore:F2}}).

                Original query: "{{originalQuery}}"

                Top retrieved snippets:
                {{topSnippets}}

                Generate 2-3 alternative search queries that might retrieve more relevant results.
                Consider:
                - Using different terminology or synonyms
                - Breaking the query into more specific sub-questions
                - Focusing on key entities or concepts

                Output exactly one query per line, no numbering, no explanations.
                """;

            string? response = await llmExpander.GenerateExpansionAsync(prompt, cancellationToken);
            if (string.IsNullOrWhiteSpace(response))
            {
                return GenerateReformulations(originalQuery);
            }

            List<string> reformulations = response
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => line.Length > 3)
                .Where(line => !line.StartsWith('-') || line.Length > 2)
                .Select(CleanReformulationLine)
                .Where(line => line.Length > 3)
                .Where(line => !string.Equals(line, originalQuery, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return reformulations.Count > 0
                ? reformulations
                : GenerateReformulations(originalQuery);
        }
        catch
        {
            return GenerateReformulations(originalQuery);
        }
    }

    /// <summary>
    /// Heuristic fallback: generates reformulations without LLM.
    /// Kept as fallback for when Ollama is unavailable or slow.
    /// </summary>
    public IReadOnlyList<string> GenerateReformulations(string originalQuery)
    {
        var words = originalQuery.Split(QuerySplitChars, StringSplitOptions.RemoveEmptyEntries);
        var keyTerms = words.Where(w => w.Length > 3).ToList();

        if (keyTerms.Count == 0)
        {
            return [originalQuery + " explained", "What is " + originalQuery + "?"];
        }

        string rearranged = string.Join(" ", keyTerms.OrderByDescending(x => x.Length));
        string withFraming = "What is known about " + string.Join(" ", keyTerms) + "?";

        return [rearranged, withFraming];
    }

    private static readonly char[] QuerySplitChars = [' ', '?', '.', ','];

    private static string CleanReformulationLine(string line)
    {
        string trimmed = line.TrimStart('-', '*', ' ', '\t');
        int dotIndex = trimmed.IndexOf('.');
        if (dotIndex > 0 && dotIndex <= 3 && char.IsDigit(trimmed[0]))
        {
            trimmed = trimmed[(dotIndex + 1)..];
        }

        return trimmed.Trim(' ', '"', '\'');
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        return text[..maxLength] + "…";
    }
}
