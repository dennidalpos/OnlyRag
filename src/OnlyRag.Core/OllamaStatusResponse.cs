namespace OnlyRag.Core;

public sealed record OllamaStatusResponse(
    string State,
    bool IsReachable,
    string BaseUrl,
    int InstalledModelCount,
    string Message,
    string? Suggestion);
