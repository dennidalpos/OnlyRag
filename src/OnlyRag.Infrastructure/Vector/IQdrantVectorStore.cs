using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Vector;

public interface IQdrantVectorStore
{
    string BackendName { get; }

    int MaxSearchableVectors { get; }

    bool IsVectorStoragePersistent { get; }

    string BuildCollectionName(string model, int dimensions);

    string BuildPointId(long chunkId);

    Task VerifyAvailabilityAsync(CancellationToken cancellationToken = default);

    Task UpsertChunkAsync(
        long chunkId,
        long documentId,
        int chunkIndex,
        string model,
        string contentHash,
        IReadOnlyList<float> vector,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string model,
        IReadOnlyList<float> queryVector,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(
        string model,
        int dimensions,
        long documentId,
        CancellationToken cancellationToken = default);
}
