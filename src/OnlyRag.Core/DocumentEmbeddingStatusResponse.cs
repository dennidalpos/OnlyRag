namespace OnlyRag.Core;

public sealed record DocumentEmbeddingStatusResponse(
    long DocumentId,
    string State,
    string? Model,
    int ChunkCount,
    int EmbeddedChunkCount,
    int ProgressPercent,
    string? CurrentJobId,
    string? CurrentStep,
    string VectorSearchBackend,
    DateTimeOffset? LastEmbeddedAtUtc);
