namespace OnlyRag.Core;

public sealed record OcrProcessingSettings(
    string Language,
    int MaxRetries,
    int PageTimeoutSeconds,
    double LowConfidenceThreshold);
