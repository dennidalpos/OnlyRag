using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public interface IHybridRetrievalService
{
    Task<DocumentSearchResponse> SearchAsync(
        DocumentSearchRequest request,
        CancellationToken cancellationToken = default);
}
