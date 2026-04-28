namespace OnlyRag.Infrastructure.Retrieval;

public sealed record KeywordSearchResponse(
    IReadOnlyList<KeywordSearchResult> Results,
    string BackendName);

public sealed record KeywordSearchResult(
    long ChunkId,
    long DocumentId,
    int ChunkIndex,
    double Score);
