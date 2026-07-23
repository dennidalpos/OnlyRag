using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed record QueryTransformationResult(
    string OriginalQuery,
    IReadOnlyList<string> ExpandedQueries,
    QueryTransformationStrategy Strategy);

public interface IQueryTransformationService
{
    Task<QueryTransformationResult> TransformAsync(
        string query,
        QueryTransformationStrategy strategy,
        CancellationToken cancellationToken = default);
}
