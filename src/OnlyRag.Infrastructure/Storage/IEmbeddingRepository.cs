namespace OnlyRag.Infrastructure.Storage;

public interface IEmbeddingRepository
{
    Task<IReadOnlyList<DocumentChunkForEmbedding>> ListChunksNeedingEmbeddingAsync(
        long documentId,
        string model,
        int afterChunkIndex,
        int take,
        CancellationToken cancellationToken = default);

    Task MarkChunkIndexedAsync(
        long chunkId,
        string model,
        string contentHash,
        int dimensions,
        string qdrantCollection,
        string qdrantPointId,
        CancellationToken cancellationToken = default);

    Task MarkChunkIndexFailedAsync(
        long chunkId,
        string model,
        string contentHash,
        int dimensions,
        string qdrantCollection,
        string qdrantPointId,
        string lastError,
        CancellationToken cancellationToken = default);

    Task<DocumentEmbeddingStatusSnapshot> GetDocumentEmbeddingStatusAsync(
        long documentId,
        string? model,
        CancellationToken cancellationToken = default);

    Task<int> CountIndexedChunksAsync(CancellationToken cancellationToken = default);
}
