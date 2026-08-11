namespace OnlyRag.Infrastructure.Retrieval;

public sealed record HybridRetrievalOptions(
    int DefaultTopK = 8,
    int KeywordTopK = 24,
    int VectorTopK = 24,
    int MaxTopK = 50,
    int SnippetMaxCharacters = 360,
    int MaxContextCharacters = 8000,
    double KeywordWeight = 0.45,
    double VectorWeight = 0.55,
    double CragConfidenceThreshold = 0.30,
    double RrfK = 60d)
{
    public static HybridRetrievalOptions Default { get; } = new();
}
