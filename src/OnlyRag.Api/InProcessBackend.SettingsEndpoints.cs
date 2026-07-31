using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Export;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settings", async (
            IOllamaSettingsService ollamaSettings,
            QdrantSettingsStore qdrantSettings,
            IPerformanceSettingsService performanceSettings,
            IngestionSettingsStore ingestionSettings,
            OcrSettingsStore ocrSettings,
            PdfExportSettingsStore pdfExportSettings,
            ILoggingService loggingService,
            CancellationToken cancellationToken) =>
        {
            var ollama = await ollamaSettings.GetAsync(cancellationToken);
            var qdrant = ToQdrantSettingsResponse(await qdrantSettings.GetAsync(cancellationToken));
            var perf = await performanceSettings.GetAsync(cancellationToken);
            var ing = await ingestionSettings.GetAsync(cancellationToken);
            var ocr = await ocrSettings.GetAsync(cancellationToken);
            var ocrProc = await ocrSettings.GetProcessingAsync(cancellationToken);
            var pdfExp = await pdfExportSettings.GetAsync(cancellationToken);
            var log = await loggingService.GetSettingsAsync(cancellationToken);

            return Results.Ok(new UnifiedSettingsResponse(
                ollama, qdrant, perf, ing, ocr, ocrProc, pdfExp, log));
        });

        app.MapPut("/api/settings", async (
            UnifiedSettingsUpdateRequest request,
            IOllamaSettingsService ollamaSettings,
            QdrantSettingsStore qdrantSettings,
            IPerformanceSettingsService performanceSettings,
            IngestionSettingsStore ingestionSettings,
            OcrSettingsStore ocrSettings,
            PdfExportSettingsStore pdfExportSettings,
            ILoggingService loggingService,
            IOcrEngine ocrEngine,
            OcrGpuCapabilityService gpuCapability,
            CancellationToken cancellationToken) =>
        {
            if (request.Ollama is not null)
            {
                await ollamaSettings.UpdateAsync(request.Ollama, cancellationToken);
            }
            if (request.Qdrant is not null)
            {
                await qdrantSettings.UpdateAsync(request.Qdrant, cancellationToken);
            }
            if (request.Performance is not null)
            {
                await performanceSettings.UpdateAsync(request.Performance, cancellationToken);
            }
            if (request.Ingestion is not null)
            {
                await ingestionSettings.UpdateAsync(request.Ingestion, cancellationToken);
            }
            if (request.Ocr is not null)
            {
                OcrSettings normalizedOcr = OcrSettings.Normalize(request.Ocr);
                if (normalizedOcr.Device == "gpu")
                {
                    OcrGpuCapabilityResponse capability = await gpuCapability.CheckAsync(ocrEngine, cancellationToken);
                    if (!capability.IsUsable)
                    {
                        return CreateBadRequestProblem(
                            "GPU OCR unavailable",
                            capability.BlockReason ?? "The GPU OCR runtime is not usable.",
                            "ocr_gpu_unavailable");
                    }
                }
                await ocrSettings.UpdateAsync(normalizedOcr, cancellationToken);
            }
            if (request.OcrProcessing is not null)
            {
                await ocrSettings.UpdateProcessingAsync(request.OcrProcessing, cancellationToken);
            }
            if (request.PdfExport is not null)
            {
                await pdfExportSettings.UpdateAsync(request.PdfExport, cancellationToken);
            }
            if (request.Logging is not null)
            {
                await loggingService.UpdateSettingsAsync(request.Logging, cancellationToken);
            }

            var ollama = await ollamaSettings.GetAsync(cancellationToken);
            var qdrant = ToQdrantSettingsResponse(await qdrantSettings.GetAsync(cancellationToken));
            var perf = await performanceSettings.GetAsync(cancellationToken);
            var ing = await ingestionSettings.GetAsync(cancellationToken);
            var ocr = await ocrSettings.GetAsync(cancellationToken);
            var ocrProc = await ocrSettings.GetProcessingAsync(cancellationToken);
            var pdfExp = await pdfExportSettings.GetAsync(cancellationToken);
            var log = await loggingService.GetSettingsAsync(cancellationToken);

            return Results.Ok(new UnifiedSettingsResponse(
                ollama, qdrant, perf, ing, ocr, ocrProc, pdfExp, log));
        });

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
                    "Invalid Qdrant settings",
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
                    "Request confirmation",
                    "Starting the local Qdrant server requires explicit confirmation.",
                    "confirmation_required");
            }

            try
            {
                return Results.Ok(await runtime.StartAsync(vectorStore, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return CreateBadRequestProblem(
                    "Qdrant not started",
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
                    "Request confirmation",
                    "Stopping the local Qdrant server requires explicit confirmation.",
                    "confirmation_required");
            }

            await runtime.StopAsync(cancellationToken);
            return Results.Ok(new OperationMessageResponse("Local Qdrant stopped."));
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
                        "GPU OCR unavailable",
                        capability.BlockReason ?? "The GPU OCR runtime is not usable.",
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
                    capability.BlockReason ?? "GPU OCR support unavailable.",
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

        app.MapGet("/api/settings/pdf-export", async (
            PdfExportSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetAsync(cancellationToken)));

        app.MapPut("/api/settings/pdf-export", async (
            PdfExportSettings request,
            PdfExportSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.UpdateAsync(request, cancellationToken)));

        app.MapGet("/api/pdf-export/status", async (
            PdfExportSettingsStore settings,
            CancellationToken cancellationToken) =>
            Results.Ok(await settings.GetStatusAsync(cancellationToken)));

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
                        ? "Connection successful. Ollama is available but no models are installed."
                        : BuildOllamaOnlineMessage(models.Count, version, runningModels.Count),
                    models.Count == 0
                        ? "Open Settings and install at least one model before using Chat or Translation."
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
                        $"Model {modelName} is already installed."));
                }

                LocalJob? existing = await FindActiveModelPullJobAsync(jobs, modelName, cancellationToken);
                if (existing is not null)
                {
                    return Results.Ok(new OllamaModelPullStartResponse(
                        existing.Id,
                        modelName,
                        existing.Status.ToString(),
                        $"Model {modelName} installation is already in progress."));
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
                    $"Model {modelName} installation started."));
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
            return Results.Ok(new OperationMessageResponse($"Model {modelName} removed."));
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
            ? " No models loaded in memory."
            : $" Models loaded in memory: {runningModelCount}.";
        return $"Connection successful. Available models: {modelCount}.{versionText}{psText}";
    }

    private static string? BuildOllamaStatusSuggestion(IReadOnlyList<OllamaRunningModelResponse> runningModels)
    {
        if (runningModels.Count == 0)
        {
            return "If a request seems slow, use 'ollama ps' to check model loading, context, and offload.";
        }

        if (runningModels.Any(model => model.SizeVram is > 0 || model.ContextLength is > 0))
        {
            return "For context, VRAM, or offload issues, compare these data with 'ollama ps' and reduce num_ctx or batch if necessary.";
        }

        return null;
    }
}
