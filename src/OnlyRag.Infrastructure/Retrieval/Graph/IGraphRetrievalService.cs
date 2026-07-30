using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval.Graph;

public interface IGraphRetrievalService
{
    Task InsertGraphAsync(
        IReadOnlyList<EntityGraphNode> nodes,
        IReadOnlyList<EntityGraphEdge> edges,
        CancellationToken cancellationToken = default);

    Task<GraphRetrievalResult> SearchGraphAsync(
        string query,
        int maxHops = 2,
        int maxNodes = 20,
        CancellationToken cancellationToken = default);
}
