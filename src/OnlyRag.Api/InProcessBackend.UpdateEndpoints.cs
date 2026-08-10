using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Update;

namespace OnlyRag.Api;

public sealed record ApplyUpdateApiRequest(string ReleaseDirectory, string ManifestPath);

public static class InProcessBackendUpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/update").WithTags("Update");

        group.MapPost("/apply", async (
            ApplyUpdateApiRequest request,
            SelectiveUpdateManager updateManager,
            CancellationToken cancellationToken) =>
        {
            UpdateResult result = await updateManager.ApplyAsync(
                request.ReleaseDirectory,
                request.ManifestPath,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/model-integrity", async (
            SelectiveUpdateManager updateManager,
            CancellationToken cancellationToken) =>
            Results.Ok(await updateManager.CheckModelIntegrityAsync(cancellationToken)));

        return endpoints;
    }
}
