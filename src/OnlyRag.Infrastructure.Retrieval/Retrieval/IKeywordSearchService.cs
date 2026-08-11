namespace OnlyRag.Infrastructure.Retrieval;

public interface IKeywordSearchService
{
    Task<KeywordSearchResponse> SearchAsync(
        string query,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default);
}
