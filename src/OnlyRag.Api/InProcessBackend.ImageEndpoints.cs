using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapImageEndpoints(this WebApplication app)
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
            ImageModelManager models,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await models.DeleteAsync(modelId, cancellationToken);
                ImageModelCatalogEntry? catalogEntry = await modelCatalog.ResetOrDeleteAsync(modelId, cancellationToken);
                return Results.Ok(new ImageModelDownloadResponse(
                    modelId,
                    catalogEntry is not null ? "ResetToDefault" : "RemovedFromCatalog",
                    catalogEntry is not null ? "Profilo integrato ripristinato ai valori predefiniti." : "Modello rimosso dal catalogo."));
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

        app.MapPost("/api/images/{id:long}/edit", async (
            long id,
            ImageEditSaveRequest request,
            ImageGenerationService imageGeneration,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await imageGeneration.SaveEditedImageAsync(id, request, cancellationToken));
            }
            catch (ImageGenerationException ex)
            {
                return MapImageGenerationException(ex, app.Services, "/api/images/edit");
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

        app.MapPost("/api/images/translate-prompt", async (
            ImagePromptTranslationRequest request,
            Ollama.IOllamaClient ollamaClient,
            Ollama.IOllamaSettingsService ollamaSettings,
            CancellationToken cancellationToken) =>
        {
            string prompt = request.Prompt?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return Results.Ok(new ImagePromptTranslationResponse(prompt, prompt, "en", WasTranslated: false));
            }

            try
            {
                OllamaSettings settings = await ollamaSettings.GetAsync(cancellationToken);
                string modelName = settings.DefaultChatModel ?? "qwen2.5:1.5b";

                IReadOnlyList<OllamaChatMessage> messages = [
                    new OllamaChatMessage("system", "You are an expert image prompt language detector and translator for Stable Diffusion / SDXL. Detect the language of the prompt. If it is NOT in English, translate it into descriptive, natural English text suitable for AI image generation. If it is already in English, return it as is. Output ONLY the final English translation without quotes or preamble."),
                    new OllamaChatMessage("user", prompt)
                ];

                string translated = await ollamaClient.GenerateChatAsync(modelName, messages, numCtx: 2048, cancellationToken: cancellationToken);
                string cleaned = translated.Trim().Trim('"').Trim('\'');
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    cleaned = prompt;
                }

                bool wasTranslated = !string.Equals(prompt, cleaned, StringComparison.OrdinalIgnoreCase);
                return Results.Ok(new ImagePromptTranslationResponse(prompt, cleaned, "en", WasTranslated: wasTranslated));
            }
            catch
            {
                // Fallback gracefully if Ollama is not configured or fails
                return Results.Ok(new ImagePromptTranslationResponse(prompt, prompt, "en", WasTranslated: false));
            }
        });
    }
}
