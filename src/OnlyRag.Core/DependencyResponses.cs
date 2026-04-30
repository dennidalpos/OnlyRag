namespace OnlyRag.Core;

public sealed record OllamaInstallStatus(
    bool CliInstalled,
    bool ApiReachable,
    string InstallCommand,
    string NetworkAccessHint);

public sealed record DependencyActionResponse(
    bool Started,
    string Message);

public sealed record OcrProvisionStatus(
    bool IsConfigured,
    bool IsRunning,
    string Message,
    string? LastError);
