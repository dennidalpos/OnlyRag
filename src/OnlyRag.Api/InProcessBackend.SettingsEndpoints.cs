using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings/ollama", async (IOllamaSettingsService settings, CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapGet("/api/settings/qdrant", async (
            QdrantSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(ToQdrantSettingsResponse(await settings.GetAsync(cancellationToken))));

        app.MapPut("/api/settings/qdrant", async (
            QdrantSettings request,
            QdrantSettingsStore settings,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(ToQdrantSettingsResponse(await settings.UpdateAsync(request, cancellationToken)));
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
            OcrSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetProcessingAsync(cancellationToken)));

        app.MapPut("/api/settings/ocr-processing", async (
            OcrProcessingSettings request,
            OcrSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.UpdateProcessingAsync(request, cancellationToken)));

        app.MapGet("/api/ocr/languages", () => Results.Ok(OcrLanguages.All));

        app.MapGet("/api/ollama/status", async (
            IOllamaClient ollamaClient,
            IOllamaSettingsService settings,
            CancellationToken cancellationToken) =>
        {
            OllamaSettings currentSettings = await settings.GetAsync(cancellationToken);

            try
            {
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                string? version = await TryGetOllamaVersionAsync(ollamaClient, cancellationToken);
                IReadOnlyList<OllamaRunningModelResponse> runningModels =
                    await TryListRunningOllamaModelsAsync(ollamaClient, cancellationToken);
                return Results.Ok(new OllamaStatusResponse(
                    "Online",
                    true,
                    currentSettings.OllamaBaseUrl,
                    models.Count,
                    models.Count == 0
                        ? "Connessione riuscita. Ollama e disponibile ma non ci sono modelli installati."
                        : BuildOllamaOnlineMessage(models.Count, version, runningModels.Count),
                    models.Count == 0
                        ? "Apri Impostazioni e installa almeno un modello prima di usare Chat o Traduzione."
                        : BuildOllamaStatusSuggestion(runningModels),
                    version,
                    runningModels));
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
            ILocalJobQueue jobs,
            CancellationToken cancellationToken) =>
        {
            try
            {
                string modelName = OllamaSettingsService.NormalizeRequiredModelName(request.Name);
                IReadOnlyList<OllamaModelSummary> models = await ollamaClient.ListModelsAsync(cancellationToken);
                if (models.Any(model =>
                    string.Equals(model.Name, modelName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(model.Model, modelName, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.Ok(new OllamaModelPullStartResponse(
                        string.Empty,
                        modelName,
                        "Installed",
                        $"Il modello {modelName} e gia installato."));
                }

                LocalJob? existing = await FindActiveModelPullJobAsync(jobs, modelName, cancellationToken);
                if (existing is not null)
                {
                    return Results.Ok(new OllamaModelPullStartResponse(
                        existing.Id,
                        modelName,
                        existing.Status.ToString(),
                        $"Installazione modello {modelName} gia in corso."));
                }

                LocalJob created = await jobs.CreateAsync(
                    new CreateLocalJobRequest(
                        OllamaModelPullJobHandler.JobType,
                        System.Text.Json.JsonSerializer.Serialize(new OllamaModelPullJobPayload(modelName)),
                        Priority: 10),
                    cancellationToken);
                return Results.Ok(new OllamaModelPullStartResponse(
                    created.Id,
                    modelName,
                    created.Status.ToString(),
                    $"Installazione modello {modelName} avviata."));
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



    private static async Task<IResult> DeleteOllamaModelAsync(
        string name,
        IOllamaClient ollamaClient,
        IOllamaSettingsService settings,
        IServiceProvider services,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            string modelName = OllamaSettingsService.NormalizeRequiredModelName(name);
            await ollamaClient.DeleteModelAsync(modelName, cancellationToken);
            await settings.ClearMissingDefaultModelAsync(modelName, cancellationToken);
            return Results.Ok(new OperationMessageResponse($"Modello {modelName} rimosso."));
        }
        catch (OllamaApiException ex)
        {
            return MapOllamaException(ex, services, operation);
        }
    }

    private static async Task<IResult> ShowOllamaModelDetailsAsync(
        string name,
        IOllamaClient ollamaClient,
        IServiceProvider services,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            OllamaModelDetails details = await ollamaClient.ShowModelAsync(name, cancellationToken);
            return Results.Ok(details);
        }
        catch (OllamaApiException ex)
        {
            return MapOllamaException(ex, services, operation);
        }
    }

    private static QdrantSettingsResponse ToQdrantSettingsResponse(QdrantSettings settings)
    {
        return new QdrantSettingsResponse(
            settings.GrpcEndpoint,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            settings.TrustNonLoopbackEndpoint,
            settings.RequireTlsForRemoteEndpoint,
            settings.UseLocalBundledServer,
            settings.LocalGrpcPort,
            settings.RequestTimeoutSeconds);
    }

    private static async Task<LocalJob?> FindActiveModelPullJobAsync(
        ILocalJobQueue jobs,
        string modelName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LocalJob> currentJobs = await jobs.ListAsync(limit: 500, cancellationToken);
        foreach (LocalJob job in currentJobs)
        {
            if (job.Type != OllamaModelPullJobHandler.JobType
                || job.Status is not (JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused))
            {
                continue;
            }

            try
            {
                OllamaModelPullJobPayload? payload =
                    System.Text.Json.JsonSerializer.Deserialize<OllamaModelPullJobPayload>(job.PayloadJson);
                if (payload is not null
                    && string.Equals(payload.ModelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    return job;
                }
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        return null;
    }

    private static async Task<string?> TryGetOllamaVersionAsync(
        IOllamaClient ollamaClient,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ollamaClient.GetVersionAsync(cancellationToken);
        }
        catch (OllamaApiException)
        {
            return null;
        }
    }

    private static async Task<IReadOnlyList<OllamaRunningModelResponse>> TryListRunningOllamaModelsAsync(
        IOllamaClient ollamaClient,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ollamaClient.ListRunningModelsAsync(cancellationToken);
        }
        catch (OllamaApiException)
        {
            return [];
        }
    }

    private static string BuildOllamaOnlineMessage(
        int modelCount,
        string? version,
        int runningModelCount)
    {
        string versionText = string.IsNullOrWhiteSpace(version) ? string.Empty : $" Versione: {version}.";
        string psText = runningModelCount == 0
            ? " Nessun modello risulta caricato in memoria."
            : $" Modelli caricati in memoria: {runningModelCount}.";
        return $"Connessione riuscita. Modelli disponibili: {modelCount}.{versionText}{psText}";
    }

    private static string? BuildOllamaStatusSuggestion(IReadOnlyList<OllamaRunningModelResponse> runningModels)
    {
        if (runningModels.Count == 0)
        {
            return "Se una richiesta sembra lenta, usa 'ollama ps' per verificare caricamento, contesto e offload dei modelli.";
        }

        if (runningModels.Any(model => model.SizeVram is > 0 || model.ContextLength is > 0))
        {
            return "Per problemi di contesto, VRAM o offload confronta questi dati con 'ollama ps' e riduci num_ctx o batch se necessario.";
        }

        return null;
    }
}
