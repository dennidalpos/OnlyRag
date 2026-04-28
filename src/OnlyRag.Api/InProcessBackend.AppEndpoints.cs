using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    private static void MapAppEndpoints(WebApplication app)
    {
        app.MapGet("/health", (BackendRuntimeState runtime) =>
            Results.Ok(new BackendHealthResponse("Healthy", runtime.StartedAtUtc)));

        app.MapGet("/api/app/status", async (
            InProcessBackendDescriptor descriptor,
            BackendRuntimeState runtime,
            IOllamaSettingsService ollamaSettings,
            IPerformanceSettingsService performanceSettings,
            ILocalJobQueue jobs,
            CancellationToken cancellationToken) =>
        {
            IReadOnlyList<LocalJob> jobList = await jobs.ListAsync(cancellationToken: cancellationToken);
            int activeJobs = jobList.Count(job =>
                job.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Paused);
            OllamaSettings settings = await ollamaSettings.GetAsync(cancellationToken);
            PerformanceSettings performance = await performanceSettings.GetAsync(cancellationToken);

            return Results.Ok(new AppStatusResponse(
                "Running",
                runtime.DatabaseStatus,
                descriptor.JobQueue.Persistent ? activeJobs.ToString() : "Disabled",
                settings.OllamaBaseUrl,
                runtime.StartedAtUtc,
                performance.EnableLowResourceMode));
        });

        app.MapGet("/api/app/storage-status", async (ILocalStorageService storage, CancellationToken cancellationToken) =>
            Results.Ok(await storage.GetStatusAsync(cancellationToken)));

        app.MapPost("/api/app/prepare-shutdown", async (
            ApplicationShutdownService shutdown,
            CancellationToken cancellationToken) =>
            Results.Ok(await shutdown.PrepareAsync(cancellationToken)));

        app.MapGet("/api/health", async (
            IVectorSearchService vectorSearch,
            IEmbeddingRepository embeddings,
            CancellationToken cancellationToken) =>
        {
            int totalEmbeddings = await embeddings.CountTotalEmbeddingsAsync(cancellationToken);
            int limit = vectorSearch.MaxSearchableVectors;
            bool nearLimit = totalEmbeddings >= (int)(limit * 0.80);
            return Results.Ok(new VectorBackendHealthResponse(
                vectorSearch.BackendName,
                vectorSearch.IsVectorStoragePersistent,
                limit,
                totalEmbeddings,
                nearLimit,
                nearLimit
                    ? $"Il database contiene {totalEmbeddings}/{limit} vettori ({(int)(totalEmbeddings * 100d / limit)}%). Avvicinandosi al limite la ricerca semantica si degraderà."
                    : null));
        });

        app.MapGet("/api/diagnostics", async (
            InProcessBackendDescriptor descriptor,
            IOllamaClient ollamaClient,
            IOllamaSettingsService ollamaSettings,
            IOcrEngine ocrEngine,
            CancellationToken cancellationToken) =>
        {
            string ollamaStatus;
            bool ollamaReachable;
            try
            {
                await ollamaClient.ListModelsAsync(cancellationToken);
                ollamaStatus = "Online";
                ollamaReachable = true;
            }
            catch (OllamaApiException ex)
            {
                ollamaStatus = ex.Kind is OllamaErrorKind.Unreachable or OllamaErrorKind.Timeout
                    ? "Offline"
                    : ex.Kind.ToString();
                ollamaReachable = false;
            }

            OcrEngineAvailability ocrAvailability = await ocrEngine.CheckAvailabilityAsync(cancellationToken);

            return Results.Ok(new DiagnosticsResponse(
                BackendLog.ResolveAppVersion(),
                descriptor.StoragePaths.DatabasePath,
                descriptor.StoragePaths.LogsDirectory,
                ollamaStatus,
                ollamaReachable,
                ocrAvailability.IsConfigured ? "Disponibile" : "Non configurato",
                ocrAvailability.IsConfigured,
                ocrAvailability.EngineName));
        });

        app.MapPost("/api/diagnostics/open-logs-folder", (InProcessBackendDescriptor descriptor) =>
        {
            try
            {
                Directory.CreateDirectory(descriptor.StoragePaths.LogsDirectory);
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false
                    }
                };
                process.StartInfo.ArgumentList.Add(descriptor.StoragePaths.LogsDirectory);
                if (!process.Start())
                {
                    return Results.Problem(
                        title: "Cartella log non aperta",
                        detail: "Windows Explorer non ha accettato la richiesta.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Ok(new OperationMessageResponse("Cartella log aperta."));
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Cartella log non aperta",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/documents/exports/open-folder", (InProcessBackendDescriptor descriptor) =>
        {
            try
            {
                Directory.CreateDirectory(descriptor.StoragePaths.DocumentExportsDirectory);
                using Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = false
                    }
                };
                process.StartInfo.ArgumentList.Add(descriptor.StoragePaths.DocumentExportsDirectory);
                if (!process.Start())
                {
                    return Results.Problem(
                        title: "Cartella export non aperta",
                        detail: "Windows Explorer non ha accettato la richiesta.",
                        statusCode: StatusCodes.Status500InternalServerError);
                }

                return Results.Ok(new OperationMessageResponse("Cartella export aperta."));
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Cartella export non aperta",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }
}
