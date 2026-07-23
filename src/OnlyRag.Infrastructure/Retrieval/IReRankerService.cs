namespace OnlyRag.Infrastructure.Retrieval;

public sealed record ReRankCandidate(
    long ChunkId,
    string Content);

public sealed record ReRankResult(
    long ChunkId,
    double Score);

public interface IReRankerService
{
    Task<IReadOnlyList<ReRankResult>> ReRankAsync(
        string query,
        IReadOnlyList<ReRankCandidate> candidates,
        CancellationToken cancellationToken = default);
}
