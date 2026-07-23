using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OnlyRag.Core;

namespace OnlyRag.Api;

internal static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspace");

        group.MapGet("/config", async (WorkspaceService service, CancellationToken ct) =>
        {
            WorkspaceConfig config = await service.GetConfigAsync(ct);
            return Results.Ok(config);
        });

        group.MapPost("/select", async (SelectWorkspaceRequest request, WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                WorkspaceConfig config = await service.SelectWorkspaceAsync(request, ct);
                return Results.Ok(config);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapGet("/files", async (WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                var files = await service.ListFilesAsync(ct);
                return Results.Ok(files);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/read-file", async (ReadWorkspaceFileRequest request, WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.ReadFileAsync(request, ct);
                return Results.Ok(result);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/write-file", async (WriteWorkspaceFileRequest request, WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                var result = await service.WriteFileAsync(request, ct);
                return Results.Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/pick-folder", async (WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                var config = await service.PickFolderAsync(ct);
                if (config is null)
                {
                    return Results.Ok(new { cancelled = true });
                }
                return Results.Ok(config);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapPost("/open-external", async (OpenExternalFileRequest request, WorkspaceService service, CancellationToken ct) =>
        {
            try
            {
                bool success = await service.OpenExternalFileAsync(request.Path, ct);
                return Results.Ok(new { success });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }
}

