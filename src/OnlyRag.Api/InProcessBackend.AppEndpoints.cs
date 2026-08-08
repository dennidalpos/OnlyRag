using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Storage.Security;
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

        app.MapGet("/api/sync/lan/discover", async (
            ILanSyncService syncService,
            CancellationToken cancellationToken) =>
        {
            var nodes = await syncService.DiscoverLanNodesAsync(cancellationToken);
            return Results.Ok(nodes);
        });

        app.MapPost("/api/sync/lan/announce", async (
            ILanSyncService syncService,
            CancellationToken cancellationToken) =>
        {
            await syncService.AnnounceNodeBeaconAsync(cancellationToken);
            return Results.Ok(new { Success = true });
        });

        app.MapHub<OnlyRag.Api.Hubs.ChatStreamHub>("/hubs/chat");
        app.MapHub<OnlyRag.Api.Hubs.JobProgressHub>("/hubs/jobs");

        app.MapGet("/api/app/storage-status", async (ILocalStorageService storage, CancellationToken cancellationToken) =>
            Results.Ok(await storage.GetStatusAsync(cancellationToken)));

        app.MapGet("/api/system/hardware", async (OnlyRag.Core.IHardwareMonitorService hardwareMonitor, CancellationToken cancellationToken) =>
            Results.Ok(await hardwareMonitor.GetMetricsAsync(cancellationToken)));

        app.MapPost("/api/system/hardware/profile", async (
            SetEnergyProfileRequest request,
            OnlyRag.Core.IHardwareMonitorService hardwareMonitor,
            CancellationToken cancellationToken) =>
        {
            await hardwareMonitor.SetEnergyProfileAsync(request.Profile, cancellationToken);
            var updatedMetrics = await hardwareMonitor.GetMetricsAsync(cancellationToken);
            return Results.Ok(updatedMetrics);
        });

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
                    "Confirmation required",
                    "A full data reset requires explicit UI confirmation.",
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
            RerankerModelManager rerankerModelManager,
            ICloudApiKeyVault cloudKeyVault,
            CancellationToken cancellationToken) =>
        {
            // Use a short timeout for Ollama status probes (not for generation)
            using var ollamaStatusCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ollamaStatusCts.CancelAfter(TimeSpan.FromSeconds(5));

            // Launch all probes in parallel — each isolated so one failure doesn't block others
            Task<(string status, bool reachable, string? version, IReadOnlyList<OllamaRunningModelResponse> runningModels)> ollamaTask =
                ProbeOllamaAsync(ollamaClient, ollamaStatusCts.Token);

            Task<QdrantStatusResponse> qdrantTask =
                RunSafeAsync(
                    () => qdrantRuntime.GetStatusAsync(qdrantVectorStore, cancellationToken),
                    new QdrantStatusResponse("Sconosciuto", false, string.Empty, false, false, false, null, null, null, null, null, null, null));

            Task<OcrEngineAvailability> ocrAvailabilityTask =
                RunSafeAsync(
                    () => diagnosticsProbeCache.CheckOcrAvailabilityAsync(ocrEngine, cancellationToken),
                    new OcrEngineAvailability(false, string.Empty, string.Empty, null));

            Task<OcrGpuCapabilityResponse> gpuTask =
                RunSafeAsync(
                    () => diagnosticsProbeCache.CheckOcrGpuCapabilityAsync(ocrGpuCapability, ocrEngine, cancellationToken),
                    new OcrGpuCapabilityResponse(false, "unknown", null, null, null, null, null, null, null, null, new Dictionary<string, string>()));

            Task<SystemTelemetryResponse> telemetryTask =
                RunSafeAsync(
                    () => diagnosticsProbeCache.CaptureSystemTelemetryAsync(systemTelemetry, cancellationToken),
                    new SystemTelemetryResponse(
                        new CpuTelemetryResponse(Environment.ProcessorCount, null),
                        new MemoryTelemetryResponse(0, 0),
                        new DiskTelemetryResponse("?", 0, 0),
                        null));

            Task<ImageGenerationRuntimeStatus> imageGenTask =
                RunSafeAsync(
                    () => imageGeneration.GetRuntimeStatusAsync(cancellationToken),
                    new ImageGenerationRuntimeStatus("Unknown", false, "CPU", string.Empty, null));

            Task<RerankerModelInfo> rerankerTask =
                RunSafeAsync(
                    () => rerankerModelManager.GetModelStatusAsync(cancellationToken),
                    new RerankerModelInfo(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, false, 0, false, null));

            CloudLlmConfiguration cloudConfig = InProcessBackendCloudLlmEndpoints.GetCurrentConfig();
            Task<string?> cloudKeyTask =
                RunSafeAsync<string?>(
                    () => cloudKeyVault.GetApiKeyAsync(cloudConfig.Provider, cancellationToken),
                    null);

            await Task.WhenAll(ollamaTask, qdrantTask, ocrAvailabilityTask, gpuTask,
                telemetryTask, imageGenTask, rerankerTask, cloudKeyTask);

            var (ollamaStatus, ollamaReachable, ollamaVersion, ollamaRunningModels) = ollamaTask.Result;
            QdrantStatusResponse qdrantStatus = qdrantTask.Result;
            OcrEngineAvailability ocrAvailability = ocrAvailabilityTask.Result;
            OcrGpuCapabilityResponse gpuCapability = gpuTask.Result;
            SystemTelemetryResponse telemetry = telemetryTask.Result;
            ImageGenerationRuntimeStatus imageGenerationStatus = imageGenTask.Result;
            RerankerModelInfo rerankerInfo = rerankerTask.Result;
            string? cloudApiKey = cloudKeyTask.Result;

            var rerankerStatus = new RerankerDiagnosticsStatus(
                rerankerInfo.IsDownloaded,
                rerankerInfo.IsDownloading,
                rerankerInfo.IsDownloaded ? "ONNX Cross-Encoder" : (rerankerInfo.IsDownloading ? "Download in corso" : "Euristico (CPU)"));

            bool cloudHasKey = !string.IsNullOrWhiteSpace(cloudApiKey);
            var cloudLlmStatus = new CloudLlmDiagnosticsStatus(
                cloudConfig.Provider.ToString(),
                cloudHasKey,
                cloudHasKey ? $"Pronto ({cloudConfig.Provider})" : "Non configurato");

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
                imageGenerationStatus,
                rerankerStatus,
                cloudLlmStatus));
        });

        app.MapGet("/api/diagnostics/startup-trace", (StartupTracer startupTracer) =>
            Results.Ok(new
            {
                TotalElapsedMs = (long)startupTracer.Elapsed.TotalMilliseconds,
                Milestones = startupTracer.GetTrace()
            }));

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
                "Confirmation required",
                "Opening local processes requires explicit UI confirmation.",
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

    private static async Task<(string status, bool reachable, string? version, IReadOnlyList<OllamaRunningModelResponse> runningModels)> ProbeOllamaAsync(
        IOllamaClient ollamaClient,
        CancellationToken cancellationToken)
    {
        try
        {
            await ollamaClient.ListModelsAsync(cancellationToken);
            string? version = await TryGetOllamaVersionAsync(ollamaClient, cancellationToken);
            IReadOnlyList<OllamaRunningModelResponse> running = await TryListRunningOllamaModelsAsync(ollamaClient, cancellationToken);
            return ("Online", true, version, running);
        }
        catch (OllamaApiException ex)
        {
            string status = ex.Kind is OllamaErrorKind.Unreachable or OllamaErrorKind.Timeout
                ? "Offline"
                : ex.Kind.ToString();
            return (status, false, null, []);
        }
        catch (OperationCanceledException)
        {
            return ("Timeout", false, null, []);
        }
    }

    private static async Task<T> RunSafeAsync<T>(Func<Task<T>> probe, T fallback)
    {
        try
        {
            return await probe();
        }
        catch
        {
            return fallback;
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
