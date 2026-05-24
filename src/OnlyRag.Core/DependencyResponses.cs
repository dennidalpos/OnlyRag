namespace OnlyRag.Core;

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
    DateTimeOffset? UpdatedAtUtc = null);

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
