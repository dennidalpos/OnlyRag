namespace OnlyRag.Infrastructure.Retrieval;

public interface IRetrievalChunkRepository
{
    Task<IReadOnlyDictionary<long, SearchChunk>> GetChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken = default);
}
