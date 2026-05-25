using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapSettingsEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings/ollama", async (IOllamaSettingsService settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapGet("/api/settings/qdrant", async (
            QdrantSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/qdrant", async (
            QdrantSettings request,
            QdrantSettingsStore settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateAsync(request, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return CreateBadRequestProblem(
                    "Impostazioni Qdrant non valide",
                    ex.Message,
                    "qdrant_settings_invalid");
            }
        });

        app.MapGet("/api/qdrant/status", async (
            QdrantLocalRuntimeService runtime,
            IQdrantVectorStore vectorStore,
            CancellationToken cancellationToken) =>
            Results.Ok(await runtime.GetStatusAsync(vectorStore, cancellationToken)));

        app.MapPost("/api/qdrant/test", async (
            QdrantLocalRuntimeService runtime,
            IQdrantVectorStore vectorStore,
            CancellationToken cancellationToken) =>
            Results.Ok(await runtime.GetStatusAsync(vectorStore, cancellationToken)));

        app.MapPost("/api/qdrant/start", async (
            ProcessLaunchRequest request,
            QdrantLocalRuntimeService runtime,
            IQdrantVectorStore vectorStore,
            CancellationToken cancellationToken) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Conferma richiesta",
                    "L'avvio del server locale Qdrant richiede conferma esplicita.",
                    "confirmation_required");
            }

            try
            {
                return Results.Ok(await runtime.StartAsync(vectorStore, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return CreateBadRequestProblem(
                    "Qdrant non avviato",
                    ex.Message,
                    "qdrant_start_failed");
            }
        });

        app.MapPost("/api/qdrant/stop", async (
            ProcessLaunchRequest request,
            QdrantLocalRuntimeService runtime,
            CancellationToken cancellationToken) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Conferma richiesta",
                    "L'arresto del server locale Qdrant richiede conferma esplicita.",
                    "confirmation_required");
            }

            await runtime.StopAsync(cancellationToken);
            return Results.Ok(new OperationMessageResponse("Qdrant locale arrestato."));
        });

        app.MapPut("/api/settings/ollama", async (
            OllamaSettings request,
            IOllamaSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateAsync(request, cancellationToken));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/settings/ollama");
            }
        });

        app.MapGet("/api/settings/performance", async (
            IPerformanceSettingsService settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/performance", async (
            PerformanceSettings request,
            IPerformanceSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateAsync(request, cancellationToken));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/settings/performance");
            }
        });

        app.MapGet("/api/settings/ocr", async (
            OcrSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/ocr", async (
            OcrSettings request,
            OcrSettingsStore settings,
            IOcrEngine ocrEngine,
            OcrGpuCapabilityService gpuCapability,
            CancellationToken cancellationToken) =>
        {
            OcrSettings normalizedRequest = OcrSettings.Normalize(request);
            if (normalizedRequest.Device == "gpu")
            {
                OcrGpuCapabilityResponse capability = await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
                if (!capability.IsUsable)
                {
                    return CreateBadRequestProblem(
                        "OCR GPU non disponibile",
                        capability.BlockReason ?? "Il runtime OCR GPU non e utilizzabile.",
                        "ocr_gpu_unavailable");
                }
            }

            return Results.Ok(await settings.UpdateAsync(normalizedRequest, cancellationToken));
        });

        app.MapPost("/api/settings/ocr/auto-enable-gpu", async (
            OcrSettingsStore settings,
            IOcrEngine ocrEngine,
            OcrGpuCapabilityService gpuCapability,
            CancellationToken cancellationToken) =>
        {
            OcrSettings currentSettings = await settings.GetAsync(cancellationToken);
            OcrGpuCapabilityResponse capability = await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
            if (!capability.IsUsable)
            {
                return Results.Ok(new OcrAutoGpuEnableResponse(
                    false,
                    capability.BlockReason ?? "Supporto OCR GPU non disponibile.",
                    currentSettings));
            }

            return Results.Ok(await settings.AutoEnableGpuAsync(cancellationToken));
        });

        app.MapGet("/api/settings/ingestion", async (
            IngestionSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/ingestion", async (
            IngestionSettings request,
            IngestionSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.UpdateAsync(request, cancellationToken)));

        app.MapGet("/api/settings/ocr-processing", async (
            OcrProcessingSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/ocr-processing", async (
            OcrProcessingSettings request,
            OcrProcessingSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.UpdateAsync(request, cancellationToken)));

        app.MapGet("/api/settings/office-conversion", async (
            OfficeConversionSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapGet("/api/ocr/languages", () => Results.Ok(OcrLanguages.All));

        app.MapPut("/api/settings/office-conversion", async (
            OfficeConversionSettings request,
            OfficeConversionSettingsStore settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await settings.UpdateAsync(request, cancellationToken));
            }
            catch (OfficeConversionException ex)
            {
                return MapOfficeConversionException(ex);
            }
        });

        app.MapGet("/api/office-converter/status", async (
            IOfficeConversionService converter,
            OfficeConversionSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await BuildOfficeConverterStatusAsync(converter, settings, cancellationToken)));

        app.MapPost("/api/office-converter/test", async (
            IOfficeConversionService converter,
            OfficeConversionSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await BuildOfficeConverterStatusAsync(converter, settings, cancellationToken)));

        app.MapGet("/api/ollama/status", async (
            IOllamaClient ollamaClient,
            IOllamaSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            OllamaSettings currentSettings = await settings.GetAsync(cancellationToken);

            try
            {
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                return Results.Ok(new OllamaStatusResponse(
                    "Online",
                    true,
                    currentSettings.OllamaBaseUrl,
                    models.Count,
                    models.Count == 0
                        ? "Connessione riuscita. Ollama e disponibile ma non ci sono modelli installati."
                        : $"Connessione riuscita. Modelli disponibili: {models.Count}.",
                    models.Count == 0
                        ? "Apri Impostazioni e installa almeno un modello prima di usare Chat o Traduzione."
                        : null));
            }
            catch (OllamaApiException ex)
            {
                return Results.Ok(CreateStatusResponse(currentSettings.OllamaBaseUrl, ex));
            }
        });

        app.MapGet("/api/ollama/models", async (IOllamaClient ollamaClient, CancellationToken cancellationToken) =>
        {
            try
            {
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                return Results.Ok(new OllamaModelsResponse(models));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/ollama/models");
            }
        });

        app.MapPost("/api/ollama/models/pull", async (
            PullOllamaModelRequest request,
            IOllamaClient ollamaClient,
            CancellationToken cancellationToken) =>
        {
            try
            {
                string modelName = OllamaSettingsService.NormalizeRequiredModelName(request.Name);
                await ollamaClient.PullModelAsync(modelName, cancellationToken);
                return Results.Ok(new OperationMessageResponse($"Modello {modelName} installato."));
            }
            catch (OllamaApiException ex)
            {
                return MapOllamaException(ex, app.Services, "/api/ollama/models/pull");
            }
        });

        app.MapDelete("/api/ollama/models/{name}", async (
            string name,
            IOllamaClient ollamaClient,
            IOllamaSettingsService settings,
            CancellationToken cancellationToken) =>
            await DeleteOllamaModelAsync(
                name,
                ollamaClient,
                settings,
                app.Services,
                "/api/ollama/models/{name}",
                cancellationToken));

        app.MapDelete("/api/ollama/models", async (
            string name,
            IOllamaClient ollamaClient,
            IOllamaSettingsService settings,
            CancellationToken cancellationToken) =>
            await DeleteOllamaModelAsync(
                name,
                ollamaClient,
                settings,
                app.Services,
                "/api/ollama/models delete",
                cancellationToken));

        app.MapGet("/api/ollama/models/{name}/details", async (
            string name,
            IOllamaClient ollamaClient,
            CancellationToken cancellationToken) =>
            await ShowOllamaModelDetailsAsync(
                name,
                ollamaClient,
                app.Services,
                "/api/ollama/models/{name}/details",
                cancellationToken));

        app.MapGet("/api/ollama/models/details", async (
            string name,
            IOllamaClient ollamaClient,
            CancellationToken cancellationToken) =>
            await ShowOllamaModelDetailsAsync(
                name,
                ollamaClient,
                app.Services,
                "/api/ollama/models/details",
                cancellationToken));
    }
}
