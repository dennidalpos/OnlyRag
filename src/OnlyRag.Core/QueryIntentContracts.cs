namespace OnlyRag.Core;

public enum QueryIntent
{
    GeneralQA = 0,
    CodeSearch = 1,
    TechnicalDocumentation = 2
}

public sealed record QueryIntentClassificationResult(
    QueryIntent Intent,
    float Confidence,
    float MinimumRerankScoreThreshold,
    int RecommendedTopK,
    IReadOnlyList<string> DetectedKeywords);

public interface IQueryIntentClassifierService
{
    QueryIntentClassificationResult ClassifyIntent(string query);
}
