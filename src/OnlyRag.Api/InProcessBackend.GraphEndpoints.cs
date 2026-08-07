using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval.Graph;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapGraphEndpoints(this WebApplication app)
    {
        app.MapGet("/api/graph/data", async Task<IResult> (
            [FromQuery] int? limit,
            [FromQuery] string? documentId,
            [FromQuery] string? entityType,
            IGraphRetrievalService graphService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await graphService.GetFullGraphAsync(
                    limit ?? 200,
                    documentId,
                    entityType,
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return CreateProblem(
                    "Graph Retrieval Failed",
                    ex.Message,
                    StatusCodes.Status500InternalServerError,
                    "graph_retrieval_failed");
            }
        });

        app.MapPost("/api/graph/search", async Task<IResult> (
            [FromBody] GraphSearchApiRequest request,
            IGraphRetrievalService graphService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return CreateBadRequestProblem(
                    "Invalid query",
                    "Enter a search query for graph traversal.",
                    "graph_query_required");
            }

            try
            {
                var result = await graphService.SearchGraphAsync(
                    request.Query,
                    request.MaxHops ?? 2,
                    request.MaxNodes ?? 20,
                    cancellationToken);

                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return CreateProblem(
                    "Graph Search Failed",
                    ex.Message,
                    StatusCodes.Status500InternalServerError,
                    "graph_search_failed");
            }
        });
    }
}

public sealed record GraphSearchApiRequest(
    string Query,
    int? MaxHops,
    int? MaxNodes);
