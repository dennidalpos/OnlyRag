namespace OnlyRag.Infrastructure.Storage;

public sealed record DocumentEmbeddingStatusSnapshot(
    long DocumentId,
    string? Model,
    int ChunkCount,
    int EmbeddedChunkCount,
    DateTimeOffset? LastEmbeddedAtUtc);
