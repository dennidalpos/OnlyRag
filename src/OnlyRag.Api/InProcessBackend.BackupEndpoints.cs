using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

public sealed record CreateBackupApiRequest(string DestinationPath, string Password);

public sealed record RestoreBackupApiRequest(string SourcePath, string Password);

public static class InProcessBackendBackupEndpoints
{
    public static IEndpointRouteBuilder MapBackupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/backup").WithTags("Backup");

        group.MapPost("/create", async (CreateBackupApiRequest request, IAesBackupService backupService, CancellationToken ct) =>
        {
            var result = await backupService.CreateEncryptedBackupAsync(request.DestinationPath, request.Password, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        group.MapPost("/restore", async (RestoreBackupApiRequest request, IAesBackupService backupService, CancellationToken ct) =>
        {
            var result = await backupService.RestoreFromEncryptedBackupAsync(request.SourcePath, request.Password, ct);
            return result.Success ? Results.Ok(result) : Results.BadRequest(result);
        });

        return endpoints;
    }
}
