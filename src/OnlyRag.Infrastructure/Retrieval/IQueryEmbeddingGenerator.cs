namespace OnlyRag.Infrastructure.Retrieval;

public interface IQueryEmbeddingGenerator
{
    Task<QueryEmbeddingResult> GenerateAsync(string query, CancellationToken cancellationToken = default);
}

public sealed record QueryEmbeddingResult(
    string Model,
    IReadOnlyList<float> Vector);

public sealed class QueryEmbeddingUnavailableException : Exception
{
    public QueryEmbeddingUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
