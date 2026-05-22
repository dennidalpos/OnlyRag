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
        app.MapGet("/health", () =>
            Results.Ok(new BackendHealthResponse("Healthy")));

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
                job.Status is JobStatus.Pending or JobStatus.Running or JobStatus.Pausing or JobStatus.Paused);
            OllamaSettings settings = await ollamaSettings.GetAsync(cancellationToken);
            PerformanceSettings performance = await performanceSettings.GetAsync(cancellationToken);

            return Results.Ok(new AppStatusResponse(
                "Running",
                runtime.DatabaseStatus,
                descriptor.JobQueue.Persistent ? activeJobs.ToString() : "Disabled",
                settings.OllamaBaseUrl,
                runtime.StartedAtUtc,
                performance.EffectiveProfile == PerformanceProfileNames.Eco));
        });

        app.MapGet("/api/app/storage-status", async (ILocalStorageService storage, CancellationToken cancellationToken) =>
            Results.Ok(await storage.GetStatusAsync(cancellationToken)));

        app.MapPost("/api/app/prepare-shutdown", async (
            ApplicationShutdownService shutdown,
            CancellationToken cancellationToken) =>
            Results.Ok(await shutdown.PrepareAsync(cancellationToken)));

        app.MapGet("/api/health", () =>
            Results.Ok(new BackendHealthResponse("Healthy")));

        app.MapGet("/api/diagnostics/vector-health", async (
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
            OcrGpuCapabilityService ocrGpuCapability,
            SystemTelemetryService systemTelemetry,
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
            OcrGpuCapabilityResponse gpuCapability = await ocrGpuCapability.CheckAsync(ocrEngine, cancellationToken);
            SystemTelemetryResponse telemetry = await systemTelemetry.CaptureAsync(cancellationToken);

            return Results.Ok(new DiagnosticsResponse(
                BackendLog.ResolveAppVersion(),
                descriptor.StoragePaths.DatabasePath,
                descriptor.StoragePaths.LogsDirectory,
                ollamaStatus,
                ollamaReachable,
                ocrAvailability.IsConfigured ? "Disponibile" : "Non configurato",
                ocrAvailability.IsConfigured,
                ocrAvailability.EngineName,
                gpuCapability,
                telemetry));
        });

        app.MapPost("/api/diagnostics/open-logs-folder", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            ILocalProcessLauncher processLauncher) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Conferma richiesta",
                    "L'apertura di processi locali richiede una conferma esplicita dalla UI.",
                    "confirmation_required");
            }

            try
            {
                Directory.CreateDirectory(descriptor.StoragePaths.LogsDirectory);
                ProcessStartInfo startInfo = CreateExplorerStartInfo(descriptor.StoragePaths.LogsDirectory);
                if (!processLauncher.TryStart(startInfo, out string? errorMessage))
                {
                    BackendLog.Write(
                        descriptor.StoragePaths,
                        $"Open logs folder failed [{httpContext.TraceIdentifier}]: {errorMessage ?? "Windows Explorer did not accept the request."}");
                    return CreateUnexpectedErrorProblem("Cartella log non aperta", httpContext.TraceIdentifier);
                }

                return Results.Ok(new OperationMessageResponse("Cartella log aperta."));
            }
            catch (Exception ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Open logs folder failed.", ex);
                return CreateUnexpectedErrorProblem("Cartella log non aperta", httpContext.TraceIdentifier);
            }
        });

        app.MapPost("/api/documents/exports/open-folder", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            ILocalProcessLauncher processLauncher) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Conferma richiesta",
                    "L'apertura di processi locali richiede una conferma esplicita dalla UI.",
                    "confirmation_required");
            }

            try
            {
                Directory.CreateDirectory(descriptor.StoragePaths.DocumentExportsDirectory);
                ProcessStartInfo startInfo = CreateExplorerStartInfo(descriptor.StoragePaths.DocumentExportsDirectory);
                if (!processLauncher.TryStart(startInfo, out string? errorMessage))
                {
                    BackendLog.Write(
                        descriptor.StoragePaths,
                        $"Open exports folder failed [{httpContext.TraceIdentifier}]: {errorMessage ?? "Windows Explorer did not accept the request."}");
                    return CreateUnexpectedErrorProblem("Cartella export non aperta", httpContext.TraceIdentifier);
                }

                return Results.Ok(new OperationMessageResponse("Cartella export aperta."));
            }
            catch (Exception ex)
            {
                BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, "Open exports folder failed.", ex);
                return CreateUnexpectedErrorProblem("Cartella export non aperta", httpContext.TraceIdentifier);
            }
        });
    }

    private static ProcessStartInfo CreateExplorerStartInfo(string folderPath)
    {
        string fullPath = Path.GetFullPath(folderPath);
        string explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        if (!File.Exists(explorerPath))
        {
            explorerPath = "explorer.exe";
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = explorerPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(fullPath);
        return startInfo;
    }
}
