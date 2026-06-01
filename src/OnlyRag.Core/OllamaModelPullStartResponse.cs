namespace OnlyRag.Core;

public sealed record OllamaModelPullStartResponse(
    string JobId,
    string ModelName,
    string Status,
    string Message);

