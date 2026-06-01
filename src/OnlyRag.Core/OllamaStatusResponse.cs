namespace OnlyRag.Core;

public sealed record OllamaStatusResponse(
    string State,
    bool IsReachable,
    string BaseUrl,
    int InstalledModelCount,
    string Message,
    string? Suggestion,
    string? Version = null,
    IReadOnlyList<OllamaRunningModelResponse>? RunningModels = null);

public sealed record OllamaRunningModelResponse(
    string Name,
    string Model,
    long? Size,
    long? SizeVram,
    string? Digest,
    int? ContextLength);
