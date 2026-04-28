namespace OnlyRag.Core;

public sealed record OllamaModelSummary(
    string Name,
    string Model,
    DateTimeOffset? ModifiedAt,
    long Size,
    string? Digest,
    string? Family,
    string? ParameterSize,
    string? QuantizationLevel);
