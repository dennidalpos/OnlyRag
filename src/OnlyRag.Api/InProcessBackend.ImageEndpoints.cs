using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Images;
using OnlyRag.Core;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapImageEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings/image-generation", async (
            IImageGenerationSettingsService settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

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

        app.MapGet("/api/images/providers/status", async (
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
            Results.Ok(await imageGeneration.GetProviderStatusesAsync(cancellationToken)));

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
    }
}

