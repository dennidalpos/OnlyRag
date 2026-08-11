namespace OnlyRag.Infrastructure.Storage;

public sealed record DocumentChunkForEmbedding(
    long Id,
    long DocumentId,
    int ChunkIndex,
    string Content,
    string ContentHash);
