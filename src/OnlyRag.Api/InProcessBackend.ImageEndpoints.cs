using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Images;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapImageEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings/image-generation", async (
            IImageGenerationSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.GetAsync(cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/settings/image-generation");
            }
        });

        app.MapPut("/api/settings/image-generation", async (
            ImageGenerationSettings request,
            IImageGenerationSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateAsync(request, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/settings/image-generation");
            }
        });

        app.MapGet("/api/images/runtime/status", async (
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
            Results.Ok(await imageGeneration.GetRuntimeStatusAsync(cancellationToken)));

        app.MapGet("/api/images/models/catalog", async (
            ImageModelManager models,
            CancellationToken cancellationToken) =>
            Results.Ok(await models.ListCatalogAsync(cancellationToken)));

        app.MapPut("/api/images/models/catalog/{modelId}", async (
            string modelId,
            ImageModelCatalogEntryRequest request,
            ImageModelCatalogStore modelCatalog,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await modelCatalog.UpsertAsync(request with { Id = modelId }, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/models/catalog");
            }
        });

        app.MapDelete("/api/images/models/catalog/{modelId}", async (
            string modelId,
            ImageModelCatalogStore modelCatalog,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await modelCatalog.ResetOrDeleteAsync(modelId, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/models/catalog");
            }
        });

        app.MapGet("/api/images/models", async (
            ImageModelManager models,
            CancellationToken cancellationToken) =>
            Results.Ok(await models.ListStatesAsync(cancellationToken)));

        app.MapPost("/api/images/models/{modelId}/download", async (
            string modelId,
            ImageModelDownloadRequest request,
            ImageModelManager models,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await models.DownloadAsync(modelId, request, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/models/download");
            }
        });

        app.MapDelete("/api/images/models/{modelId}/download", async (
            string modelId,
            ImageModelManager models,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await models.CancelDownloadAsync(modelId, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/models/download");
            }
        });

        app.MapDelete("/api/images/models/{modelId}", async (
            string modelId,
            ImageModelManager models,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await models.DeleteAsync(modelId, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/models");
            }
        });

        app.MapGet("/api/images", async (
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
            Results.Ok(await imageGeneration.ListAsync(cancellationToken)));

        app.MapPost("/api/images/generate", async (
            ImageGenerationRequest request,
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await imageGeneration.GenerateAsync(request, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/generate");
            }
        });

        app.MapGet("/api/images/{id:long}/file", async (
            long id,
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
        {
            (GeneratedImage Image, string AbsolutePath)? file = await imageGeneration.GetFileAsync(id, cancellationToken);
            return file is null
                ? CreateNotFoundProblem("Immagine")
                : Results.File(file.Value.AbsolutePath, file.Value.Image.MimeType, file.Value.Image.FileName);
        });

        app.MapDelete("/api/images/{id:long}", async (
            long id,
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
        {
            GeneratedImage? deleted = await imageGeneration.DeleteAsync(id, cancellationToken);
            return deleted is null ? CreateNotFoundProblem("Immagine") : Results.Ok(deleted);
        });

        app.MapPost("/api/images/{id:long}/crop", async (
            long id,
            ImageCropSaveRequest request,
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await imageGeneration.SaveCroppedImageAsync(id, request, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/crop");
            }
        });

        app.MapPost("/api/images/open-folder", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            ILocalProcessLauncher processLauncher,
            ImageGenerationService imageGeneration) =>
        {
            return OpenConfirmedFolder(
                httpContext,
                request,
                descriptor,
                processLauncher,
                imageGeneration.GetGeneratedRoot(),
                "Open generated images folder",
                "Cartella immagini generate aperta.",
                "Cartella immagini generate non aperta");
        });
    }
}
