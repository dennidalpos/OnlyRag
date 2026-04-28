using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface IVectorSearchService
{
    string BackendName { get; }

    int MaxSearchableVectors { get; }

    bool IsVectorStoragePersistent { get; }

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string model,
        IReadOnlyList<float> queryVector,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default);
}
