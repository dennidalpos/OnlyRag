
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    internal static void MapAppEndpoints(this WebApplication app)
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
            int activeJobs = jobList.Count(job => job.Status.IsActive());
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

        app.MapPost("/api/app/reset-on-next-startup", (
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor) =>
        {
            if (!request.Confirmed)
            {
                return CreateBadRequestProblem(
                    "Conferma richiesta",
                    "Il reset totale dei dati locali richiede conferma esplicita dalla UI.",
                    "confirmation_required");
            }

            AppDataReset.RequestResetOnNextStartup(descriptor.StoragePaths);
            return Results.Ok(new OperationMessageResponse(
                "Reset dati pianificato. Al riavvio verra creato un backup timestamped prima di cancellare dati locali, profilo WebView2, cache, log e impostazioni."));
        });

        app.MapGet("/api/health", () =>
            Results.Ok(new BackendHealthResponse("Healthy")));

        app.MapGet("/api/diagnostics/vector-health", async (
            IQdrantVectorStore vectorSearch,
            IEmbeddingRepository embeddings,
            CancellationToken cancellationToken) =>
        {
            int totalEmbeddings = await embeddings.CountIndexedChunksAsync(cancellationToken);
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
            QdrantLocalRuntimeService qdrantRuntime,
            IQdrantVectorStore qdrantVectorStore,
            IOcrEngine ocrEngine,
            ImageGenerationService imageGeneration,
            OcrGpuCapabilityService ocrGpuCapability,
            SystemTelemetryService systemTelemetry,
            DiagnosticsProbeCacheService diagnosticsProbeCache,
            CancellationToken cancellationToken) =>
        {
            string ollamaStatus;
            bool ollamaReachable;
            string? ollamaVersion = null;
            IReadOnlyList<OllamaRunningModelResponse> ollamaRunningModels = [];
            try
            {
                await ollamaClient.ListModelsAsync(cancellationToken);
                ollamaVersion = await TryGetOllamaVersionAsync(ollamaClient, cancellationToken);
                ollamaRunningModels = await TryListRunningOllamaModelsAsync(ollamaClient, cancellationToken);
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

            QdrantStatusResponse qdrantStatus = await qdrantRuntime.EnsureLocalServerAsync(qdrantVectorStore, cancellationToken);
            OcrEngineAvailability ocrAvailability = await diagnosticsProbeCache.CheckOcrAvailabilityAsync(ocrEngine, cancellationToken);
            OcrGpuCapabilityResponse gpuCapability = await diagnosticsProbeCache.CheckOcrGpuCapabilityAsync(ocrGpuCapability, ocrEngine, cancellationToken);
            SystemTelemetryResponse telemetry = await diagnosticsProbeCache.CaptureSystemTelemetryAsync(systemTelemetry, cancellationToken);
            ImageGenerationRuntimeStatus imageGenerationStatus = await imageGeneration.GetRuntimeStatusAsync(cancellationToken);

            return Results.Ok(new DiagnosticsResponse(
                BackendLog.ResolveAppVersion(),
                descriptor.StoragePaths.DatabasePath,
                descriptor.StoragePaths.LogsDirectory,
                ollamaStatus,
                ollamaReachable,
                qdrantStatus,
                ocrAvailability.IsConfigured ? "Disponibile" : "Non configurato",
                ocrAvailability.IsConfigured,
                ocrAvailability.EngineName,
                gpuCapability,
                telemetry,
                ollamaVersion,
                ollamaRunningModels,
                imageGenerationStatus));
        });

        app.MapPost("/api/diagnostics/open-logs-folder", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            ILocalProcessLauncher processLauncher) =>
        {
            return OpenConfirmedFolder(
                httpContext,
                request,
                descriptor,
                processLauncher,
                descriptor.StoragePaths.LogsDirectory,
                "Open logs folder",
                "Cartella log aperta.",
                "Cartella log non aperta");
        });

        app.MapPost("/api/documents/exports/open-folder", (
            HttpContext httpContext,
            ProcessLaunchRequest request,
            InProcessBackendDescriptor descriptor,
            ILocalProcessLauncher processLauncher) =>
        {
            return OpenConfirmedFolder(
                httpContext,
                request,
                descriptor,
                processLauncher,
                descriptor.StoragePaths.DocumentExportsDirectory,
                "Open exports folder",
                "Cartella export aperta.",
                "Cartella export non aperta");
        });
    }

    private static IResult OpenConfirmedFolder(
        HttpContext httpContext,
        ProcessLaunchRequest request,
        InProcessBackendDescriptor descriptor,
        ILocalProcessLauncher processLauncher,
        string folderPath,
        string operationName,
        string successMessage,
        string failureTitle)
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
            Directory.CreateDirectory(folderPath);
            ProcessStartInfo startInfo = CreateExplorerStartInfo(folderPath);
            if (!processLauncher.TryStart(startInfo, out string? errorMessage))
            {
                BackendLog.Write(
                    descriptor.StoragePaths,
                    $"{operationName} failed [{httpContext.TraceIdentifier}]: {errorMessage ?? "Windows Explorer did not accept the request."}");
                return CreateUnexpectedErrorProblem(failureTitle, httpContext.TraceIdentifier);
            }

            return Results.Ok(new OperationMessageResponse(successMessage));
        }
        catch (Exception ex)
        {
            BackendLog.WriteException(descriptor.StoragePaths, httpContext.TraceIdentifier, $"{operationName} failed.", ex);
            return CreateUnexpectedErrorProblem(failureTitle, httpContext.TraceIdentifier);
        }
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
