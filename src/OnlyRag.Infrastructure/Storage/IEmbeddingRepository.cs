namespace OnlyRag.Infrastructure.Storage;

public interface IEmbeddingRepository
{
    Task<IReadOnlyList<DocumentChunkForEmbedding>> ListChunksNeedingEmbeddingAsync(
        long documentId,
        string model,
        int afterChunkIndex,
        int take,
        CancellationToken cancellationToken = default);

    Task UpsertEmbeddingAsync(
        long chunkId,
        string model,
        string contentHash,
        IReadOnlyList<float> vector,
        CancellationToken cancellationToken = default);

    Task<DocumentEmbeddingStatusSnapshot> GetDocumentEmbeddingStatusAsync(
        long documentId,
        string? model,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredEmbeddingVector>> ListEmbeddingVectorsAsync(
        string model,
        long afterChunkId,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> CountTotalEmbeddingsAsync(CancellationToken cancellationToken = default);
}
