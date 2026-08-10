namespace OnlyRag.Core;

public sealed record BackendHealthResponse(string Status);

public sealed record AppStatusResponse(
    string Backend,
    string Database,
    string JobQueue,
    string Ollama,
    DateTimeOffset StartedAtUtc,
    bool LowResourceMode);

public sealed record AppShutdownPreparationResponse(
    int ActiveJobCount,
    int CancelledJobCount,
    string[] UnstoppedJobIds)
{
    public bool IsComplete => UnstoppedJobIds.Length == 0;
}

public sealed record RerankerDiagnosticsStatus(
    bool IsDownloaded,
    bool IsDownloading,
    string StatusText);

public sealed record CloudLlmDiagnosticsStatus(
    string Provider,
    bool HasApiKey,
    string StatusText);

public sealed record DiagnosticsResponse(
    string AppVersion,
    string DatabasePath,
    string LogsDirectory,
    string OllamaStatus,
    bool OllamaIsReachable,
    QdrantStatusResponse Qdrant,
    string OcrStatus,
    bool OcrIsConfigured,
    string OcrEngineName,
    OcrGpuCapabilityResponse OcrGpuCapability,
    SystemTelemetryResponse SystemTelemetry,
    string? OllamaVersion = null,
    IReadOnlyList<OllamaRunningModelResponse>? OllamaRunningModels = null,
    ImageGenerationRuntimeStatus? ImageGeneration = null,
    RerankerDiagnosticsStatus? Reranker = null,
    CloudLlmDiagnosticsStatus? CloudLlm = null);

public sealed record OcrGpuCapabilityResponse(
    bool IsUsable,
    string Status,
    string? BlockReason,
    string? RuntimeDetail,
    string? EngineVersion,
    string? NvidiaName,
    string? DriverVersion,
    bool? CompiledWithCuda,
    int? CudaDeviceCount,
    string? ActiveDevice,
    IReadOnlyDictionary<string, string> PackageVersions,
    string CapabilityStatus = "unknown");

public sealed record SystemTelemetryResponse(
    CpuTelemetryResponse Cpu,
    MemoryTelemetryResponse Memory,
    DiskTelemetryResponse SystemDisk,
    GpuTelemetryResponse? Gpu);

public sealed record CpuTelemetryResponse(
    int LogicalProcessorCount,
    double? UsagePercent);

public sealed record MemoryTelemetryResponse(
    long TotalBytes,
    long AvailableBytes);

public sealed record DiskTelemetryResponse(
    string Name,
    long TotalBytes,
    long AvailableBytes);

public sealed record GpuTelemetryResponse(
    string Name,
    string DriverVersion,
    double? UsagePercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);

public sealed record OllamaInstallStatus(
    bool CliInstalled,
    bool ApiReachable,
    string InstallCommand,
    string NetworkAccessHint);

public sealed record DependencyActionResponse(
    bool Started,
    string Message);

public sealed record OcrProvisionRequest(
    bool Confirmed,
    string? RuntimeTarget = null);

public sealed record OcrProvisionStatus(
    bool IsConfigured,
    bool IsRunning,
    string Message,
    string? LastError,
    string RuntimeTarget = "auto",
    string ResolvedRuntime = "unknown",
    string? RuntimeDetail = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? UpdatedAtUtc = null,
    string? StepKey = null,
    string? StepLabel = null,
    int StepIndex = 0,
    int StepCount = 0,
    int ProgressPercent = 0,
    string Severity = "info",
    bool CanRetry = false,
    string? SelectedRuntime = null,
    bool IsAutomaticRepair = false);

public sealed record OcrStartupAnalysisResponse(
    bool ShouldPrompt,
    bool IsWindowsSupported,
    bool HasMinimumDiskSpace,
    long AvailableDiskBytes,
    long RequiredDiskBytes,
    bool HasCompatiblePython,
    bool IsOcrConfigured,
    bool IsNvidiaRuntimeAvailable,
    bool IsGpuUsable,
    string RecommendedRuntimeTarget,
    string Title,
    string Message,
    IReadOnlyList<string> Findings);

public sealed record OcrAutoGpuEnableResponse(
    bool Applied,
    string Message,
    OcrSettings Settings);

public sealed record StorageStatusResponse(
    string Provider,
    string DatabasePath,
    bool DatabaseExists,
    int CurrentSchemaVersion,
    int TargetSchemaVersion,
    string SchemaStatus,
    bool Fts5Available,
    string? TechnicalNote);

public sealed record PdfExportConverterStatusResponse(
    string State,
    bool IsAvailable,
    string? ExecutablePath,
    string Message,
    string? Suggestion,
    int ConversionTimeoutSeconds);

public sealed record LogEntry(
    string Id,
    DateTime TimestampUtc,
    AppLogLevel Level,
    string Category,
    string Message,
    string? ExceptionDetails = null,
    string? DataJson = null);

public sealed record LogStorageInfo(
    long TotalSizeBytes,
    string FormattedSize,
    int MemoryEntryCount,
    int FileCount,
    string LogDirectory);

public static class AppDataReset
{
    public static void RequestResetOnNextStartup(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(
            paths.PendingResetMarkerPath,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool ApplyPendingReset(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!File.Exists(paths.PendingResetMarkerPath))
        {
            return false;
        }

        Logging.EarlyBootstrapperLogger.Close();
        DeleteDirectoryContents(paths.DataRoot);
        return true;
    }

    public static void ResetNow(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Logging.EarlyBootstrapperLogger.Close();
        DeleteDirectoryContents(paths.DataRoot);
    }

    private static void DeleteDirectoryContents(string dataRoot)
    {
        string fullRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(fullRoot))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(fullRoot))
        {
            DeleteFileSystemEntrySafe(entry);
        }
    }

    private static void DeleteFileSystemEntrySafe(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                ClearAttributesRecursive(path);
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                {
                    DeleteFileSystemEntrySafe(entry);
                }

                try
                {
                    Directory.Delete(path, recursive: false);
                }
                catch
                {
                    // Ignore directory deletion failure if a locked file inside could not be deleted
                }
            }
            else if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Ignore locked active files (e.g. startup-bootstrap.log or active log files) during data reset
        }
    }

    private static void ClearAttributesRecursive(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(childDirectory, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }
}

public sealed record ProcessLaunchRequest(bool Confirmed);

public sealed record OperationMessageResponse(string Message);

public static class OnlyRagApiHeaders
{
    public const string SessionTokenHeaderName = "X-OnlyRag-Session-Token";
}

public static class OnlyRagWebOrigins
{
    public const string StaticWebViewHostName = "app.onlyrag.local";

    public const string StaticWebViewOrigin = "https://" + StaticWebViewHostName;
}

public sealed record AppStoragePaths(
    string DataRoot,
    string DataDirectory,
    string DatabasePath,
    string DocumentsRoot,
    string DocumentOriginalsDirectory,
    string DocumentRendersDirectory,
    string DocumentOcrCacheDirectory,
    string DocumentExportsDirectory,
    string ImageModelsDirectory,
    string RerankerModelsDirectory,
    string LogsDirectory,
    string WebView2UserDataDirectory,
    string TempDirectory)
{
    public const string ProductName = "OnlyRag";
    public string PendingResetMarkerPath => Path.Combine(DataRoot, ".reset-on-startup");

    public static AppStoragePaths FromLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return FromRoot(Path.Combine(localAppData, ProductName));
    }

    public static AppStoragePaths FromRoot(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        string normalizedRoot = Path.GetFullPath(dataRoot);
        string dataDirectory = Path.Combine(normalizedRoot, "data");
        string documentsRoot = Path.Combine(normalizedRoot, "documents");

        return new AppStoragePaths(
            normalizedRoot,
            dataDirectory,
            Path.Combine(dataDirectory, "onlyrag.db"),
            documentsRoot,
            Path.Combine(documentsRoot, "originals"),
            Path.Combine(documentsRoot, "renders"),
            Path.Combine(documentsRoot, "ocr-cache"),
            Path.Combine(documentsRoot, "exports"),
            Path.Combine(normalizedRoot, "models", "images"),
            Path.Combine(normalizedRoot, "models", "reranker"),
            Path.Combine(normalizedRoot, "logs"),
            Path.Combine(normalizedRoot, "webview2"),
            Path.Combine(normalizedRoot, "temp"));
    }

    public IEnumerable<string> EnumerateRequiredDirectories()
    {
        yield return DataRoot;
        yield return DataDirectory;
        yield return DocumentOriginalsDirectory;
        yield return DocumentRendersDirectory;
        yield return DocumentOcrCacheDirectory;
        yield return DocumentExportsDirectory;
        yield return ImageModelsDirectory;
        yield return RerankerModelsDirectory;
        yield return LogsDirectory;
        yield return WebView2UserDataDirectory;
        yield return TempDirectory;
    }
}
