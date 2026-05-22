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
    string? RuntimeDetail = null);
