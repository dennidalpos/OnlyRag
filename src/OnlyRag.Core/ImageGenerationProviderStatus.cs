namespace OnlyRag.Core;

public sealed record ImageGenerationProviderStatus(
    string Provider,
    string State,
    bool IsReachable,
    string BaseUrl,
    string Message,
    string? Suggestion);

