namespace OnlyRag.Infrastructure.Storage;

public sealed record StoredEmbeddingVector(
    long ChunkId,
    long DocumentId,
    int ChunkIndex,
    string Model,
    string ContentHash,
    float[] Vector);
