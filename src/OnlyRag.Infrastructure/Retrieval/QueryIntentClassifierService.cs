using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class QueryIntentClassifierService : IQueryIntentClassifierService
{
    private static readonly HashSet<string> CodeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "function", "class", "interface", "struct", "method", "async", "await", "lambda",
        "override", "public", "private", "protected", "import", "using", "return", "exception",
        "nullreference", "stacktrace", "refactor", "bug", "sql", "query", "script", "var", "const",
        "let", "regex", "enum", "type", "void", "namespace"
    };

    private static readonly HashSet<string> TechnicalDocKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "architecture", "config", "configuration", "install", "setup", "deploy", "deployment",
        "guide", "sdk", "endpoint", "swagger", "overview", "readme", "docs", "specification",
        "manual", "pipeline", "schema", "database", "orm", "routing", "security", "auth"
    };

    private static readonly Regex CodePatternRegex = new(
        @"(?:[\{\}\(\)\[\];<>=+\-*/%&|^!~]|=>|->|\b(?:if|else|for|while|switch|case|try|catch|throw|return)\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WordTokenRegex = new(@"\b[a-zA-Z0-9_]+\b", RegexOptions.Compiled);

    public QueryIntentClassificationResult ClassifyIntent(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryIntentClassificationResult(
                QueryIntent.GeneralQA,
                Confidence: 1.0f,
                MinimumRerankScoreThreshold: 0.35f,
                RecommendedTopK: 8,
                DetectedKeywords: []);
        }

        var matches = WordTokenRegex.Matches(query);
        List<string> detectedCode = [];
        List<string> detectedTech = [];

        foreach (Match match in matches)
        {
            string token = match.Value;
            if (CodeKeywords.Contains(token))
            {
                detectedCode.Add(token);
            }
            if (TechnicalDocKeywords.Contains(token))
            {
                detectedTech.Add(token);
            }
        }

        int codePatternMatches = CodePatternRegex.Count(query);
        int codeScore = detectedCode.Count * 2 + (codePatternMatches > 2 ? 3 : codePatternMatches);
        int techScore = detectedTech.Count * 2;

        if (codeScore > techScore && codeScore >= 2)
        {
            float confidence = Math.Min(1.0f, 0.5f + (codeScore * 0.1f));
            return new QueryIntentClassificationResult(
                QueryIntent.CodeSearch,
                Confidence: confidence,
                MinimumRerankScoreThreshold: 0.45f,
                RecommendedTopK: 5,
                DetectedKeywords: detectedCode.Distinct().ToList());
        }

        if (techScore >= 2)
        {
            float confidence = Math.Min(1.0f, 0.5f + (techScore * 0.1f));
            return new QueryIntentClassificationResult(
                QueryIntent.TechnicalDocumentation,
                Confidence: confidence,
                MinimumRerankScoreThreshold: 0.25f,
                RecommendedTopK: 10,
                DetectedKeywords: detectedTech.Distinct().ToList());
        }

        return new QueryIntentClassificationResult(
            QueryIntent.GeneralQA,
            Confidence: 0.8f,
            MinimumRerankScoreThreshold: 0.35f,
            RecommendedTopK: 8,
            DetectedKeywords: []);
    }
}
