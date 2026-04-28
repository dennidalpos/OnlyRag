namespace OnlyRag.Core;

public sealed record VectorSearchResult(
    long ChunkId,
    long DocumentId,
    int ChunkIndex,
    double Score);
