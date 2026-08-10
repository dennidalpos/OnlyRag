using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
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
            AppStoragePaths storagePaths,
            CancellationToken cancellationToken) =>
        {
            string stagingRoot = Path.GetFullPath(Path.Combine(storagePaths.DataRoot, "updates", "staging"));
            string releaseDirectory = Path.GetFullPath(request.ReleaseDirectory);
            string manifestPath = Path.GetFullPath(request.ManifestPath);
            if (!IsPathUnder(releaseDirectory, stagingRoot)
                || !IsPathUnder(manifestPath, releaseDirectory))
            {
                return Results.Problem(
                    detail: "Gli aggiornamenti devono provenire dalla staging directory locale attendibile.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            UpdateResult result = await updateManager.ApplyAsync(
                releaseDirectory,
                manifestPath,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/model-integrity", async (
            SelectiveUpdateManager updateManager,
            CancellationToken cancellationToken) =>
            Results.Ok(await updateManager.CheckModelIntegrityAsync(cancellationToken)));

        return endpoints;
    }

    private static bool IsPathUnder(string path, string root)
    {
        string normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
