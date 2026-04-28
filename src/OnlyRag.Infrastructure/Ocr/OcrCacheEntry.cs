namespace OnlyRag.Infrastructure.Ocr;

public sealed record OcrCacheEntry(
    string CacheKey,
    string PageHash,
    string EngineName,
    string EngineVersion,
    string Language,
    string PreprocessVersion,
    string Text,
    string? BoxesJson,
    double? Confidence,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
