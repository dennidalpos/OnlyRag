namespace OnlyRag.Core;

public sealed record OllamaModelPullProgress(
    string Status,
    long? Total,
    long? Completed,
    int? ProgressPercent,
    string? Digest = null,
    string? Layer = null);
