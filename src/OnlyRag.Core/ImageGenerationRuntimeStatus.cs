namespace OnlyRag.Core;

public sealed record ImageGenerationRuntimeStatus(
    string State,
    bool IsReady,
    string ExecutionProvider,
    string Message,
    string? Suggestion);
