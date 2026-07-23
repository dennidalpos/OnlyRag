using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed record CragEvaluationResult(
    bool IsConfident,
    double HighestScore,
    string SummaryNotice);

public sealed class CragEvaluator
{
    public CragEvaluationResult Evaluate(
        IReadOnlyList<DocumentSearchResult> results,
        double threshold = 0.30)
    {
        if (results.Count == 0)
        {
            return new CragEvaluationResult(false, 0d, "Nessun candidato recuperato.");
        }

        double maxScore = results.Max(r => r.ReRankScore ?? r.Score);
        if (maxScore < threshold)
        {
            return new CragEvaluationResult(
                false,
                maxScore,
                $"Punteggio di confidenza RAG basso ({maxScore:F2} < {threshold:F2}). Considera di riformulare la domanda.");
        }

        return new CragEvaluationResult(
            true,
            maxScore,
            $"Confidenza RAG elevata ({maxScore:F2}).");
    }
}
