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
            // Launch all probes in parallel — each isolated so one failure doesn't block others
            Task<ProbeResult<(string status, bool reachable, string? version, IReadOnlyList<OllamaRunningModelResponse> runningModels)>> ollamaTask =
                RunTimedSafeAsync(
                    "Ollama",
                    token => ProbeOllamaAsync(ollamaClient, token),
                    ("Timeout", false, null, []),
                    result => result.reachable ? ("online", null) : result.status == "Timeout" ? ("timeout", result.status) : ("offline", result.status),
                    TimeSpan.FromSeconds(10),
                    cancellationToken);

            Task<ProbeResult<QdrantStatusResponse>> qdrantTask =
                RunTimedSafeAsync(
                    "Qdrant",
                    token => qdrantRuntime.GetStatusAsync(qdrantVectorStore, token),
                    new QdrantStatusResponse("Sconosciuto", false, string.Empty, false, false, false, null, null, null, null, null, null, null),
                    result => result.IsReachable ? ("online", null) : result.Status is "Caricamento" or "Starting" ? ("starting", result.Status) : ("offline", result.Error ?? result.Status),
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

            Task<ProbeResult<OcrEngineAvailability>> ocrAvailabilityTask =
                RunTimedSafeAsync(
                    "OCR",
                    token => diagnosticsProbeCache.CheckOcrAvailabilityAsync(ocrEngine, token),
                    new OcrEngineAvailability(false, string.Empty, string.Empty, null),
                    result => result.IsConfigured ? ("online", null) : ("not_configured", result.Message),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            Task<ProbeResult<OcrGpuCapabilityResponse>> gpuTask =
                RunTimedSafeAsync(
                    "OCR GPU",
                    token => diagnosticsProbeCache.CheckOcrGpuCapabilityAsync(ocrGpuCapability, ocrEngine, token),
                    new OcrGpuCapabilityResponse(false, "unknown", null, null, null, null, null, null, null, null, new Dictionary<string, string>()),
                    result => result.IsUsable ? ("online", null) : (result.CapabilityStatus == "no_nvidia_gpu" ? ("not_configured", result.BlockReason) : ("offline", result.BlockReason ?? result.Status)),
                    TimeSpan.FromSeconds(5),
                    cancellationToken);

            Task<ProbeResult<SystemTelemetryResponse>> telemetryTask =
                RunTimedSafeAsync(
                    "Telemetria",
                    token => diagnosticsProbeCache.CaptureSystemTelemetryAsync(systemTelemetry, token),
                    new SystemTelemetryResponse(
                        new CpuTelemetryResponse(Environment.ProcessorCount, null),
                        new MemoryTelemetryResponse(0, 0),
                        new DiskTelemetryResponse("?", 0, 0),
                        null),
                    _ => ("online", null),
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

            Task<ProbeResult<ImageGenerationRuntimeStatus>> imageGenTask =
                RunTimedSafeAsync(
                    "Immagini",
                    token => imageGeneration.GetRuntimeStatusAsync(token),
                    new ImageGenerationRuntimeStatus("Unknown", false, "CPU", string.Empty, null),
                    result => result.IsReady ? ("online", null) : ("not_configured", result.Suggestion ?? result.Message),
                    TimeSpan.FromSeconds(3),
                    cancellationToken);

            Task<ProbeResult<RerankerModelInfo>> rerankerTask =
                RunTimedSafeAsync(
                    "Reranker",
                    token => rerankerModelManager.GetModelStatusAsync(token),
                    new RerankerModelInfo(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, false, 0, false, null),
                    result => result.IsDownloading ? ("starting", "Download in corso") : result.IsDownloaded ? ("online", null) : ("not_configured", "Modello non installato"),
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

            CloudLlmConfiguration cloudConfig = InProcessBackendCloudLlmEndpoints.GetCurrentConfig();
            Task<ProbeResult<string?>> cloudKeyTask =
                RunTimedSafeAsync<string?>(
                    "Cloud LLM",
                    token => cloudKeyVault.GetApiKeyAsync(cloudConfig.Provider, token),
                    null,
                    result => result is null ? ("not_configured", "API key non configurata") : ("online", null),
                    TimeSpan.FromSeconds(2),
                    cancellationToken);

            await Task.WhenAll(ollamaTask, qdrantTask, ocrAvailabilityTask, gpuTask,
                telemetryTask, imageGenTask, rerankerTask, cloudKeyTask);

            var (ollamaStatus, ollamaReachable, ollamaVersion, ollamaRunningModels) = ollamaTask.Result.Value;
            QdrantStatusResponse qdrantStatus = qdrantTask.Result.Value;
            OcrEngineAvailability ocrAvailability = ocrAvailabilityTask.Result.Value;
            OcrGpuCapabilityResponse gpuCapability = gpuTask.Result.Value;
            SystemTelemetryResponse telemetry = telemetryTask.Result.Value;
            ImageGenerationRuntimeStatus imageGenerationStatus = imageGenTask.Result.Value;
            RerankerModelInfo rerankerInfo = rerankerTask.Result.Value;
            string? cloudApiKey = cloudKeyTask.Result.Value;

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
                cloudLlmStatus,
                [
                    ollamaTask.Result.Status,
                    qdrantTask.Result.Status,
                    ocrAvailabilityTask.Result.Status,
                    gpuTask.Result.Status,
                    telemetryTask.Result.Status,
                    imageGenTask.Result.Status,
                    rerankerTask.Result.Status,
                    cloudKeyTask.Result.Status
                ]));
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
            Task listTask = ollamaClient.ListModelsAsync(cancellationToken);
            Task<string?> versionTask = TryGetOllamaVersionAsync(ollamaClient, cancellationToken);
            Task<IReadOnlyList<OllamaRunningModelResponse>> runningTask = TryListRunningOllamaModelsAsync(ollamaClient, cancellationToken);

            await listTask;

            string? version = versionTask.IsCompletedSuccessfully ? versionTask.Result : null;
            IReadOnlyList<OllamaRunningModelResponse> running = runningTask.IsCompletedSuccessfully ? runningTask.Result : [];

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

    private static async Task<ProbeResult<T>> RunTimedSafeAsync<T>(
        string module,
        Func<CancellationToken, Task<T>> probe,
        T fallback,
        Func<T, (string state, string? error)> classify,
        TimeSpan timeout,
        CancellationToken requestCancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            T value = await probe(timeoutSource.Token);
            (string state, string? error) = classify(value);
            return new ProbeResult<T>(
                value,
                new DiagnosticsModuleStatus(module, state, stopwatch.ElapsedMilliseconds, error));
        }
        catch (OperationCanceledException)
        {
            if (requestCancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProbeResult<T>(
                fallback,
                new DiagnosticsModuleStatus(module, "timeout", stopwatch.ElapsedMilliseconds, $"Timeout dopo {timeout.TotalSeconds:0} s."));
        }
        catch (Exception ex)
        {
            return new ProbeResult<T>(
                fallback,
                new DiagnosticsModuleStatus(module, "error", stopwatch.ElapsedMilliseconds, ex.Message));
        }
    }

    private sealed record ProbeResult<T>(T Value, DiagnosticsModuleStatus Status);

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
