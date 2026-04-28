using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ocr;

public sealed record OcrPipelineOptions(
    string Language,
    int MaxRetries,
    TimeSpan PageTimeout,
    double LowConfidenceThreshold,
    int MaxParallelPages,
    OcrSettings Settings)
{
    public const string DefaultLanguage = OcrLanguages.DefaultCode;
    public const int DefaultMaxRetries = 2;
    public const int DefaultMaxParallelPages = 1;
    public static readonly TimeSpan DefaultPageTimeout = TimeSpan.FromSeconds(180);
    public const double DefaultLowConfidenceThreshold = 0.55d;

    public static OcrPipelineOptions Default { get; } = new(
        DefaultLanguage,
        DefaultMaxRetries,
        DefaultPageTimeout,
        DefaultLowConfidenceThreshold,
        DefaultMaxParallelPages,
        OcrSettings.Default);

    public static OcrPipelineOptions Normalize(
        string? language,
        int? maxRetries,
        int? pageTimeoutSeconds,
        double? lowConfidenceThreshold,
        int? maxParallelPages = null,
        OcrSettings? settings = null)
    {
        string normalizedLanguage = OcrLanguages.NormalizeCode(language);
        int normalizedRetries = Math.Clamp(maxRetries ?? DefaultMaxRetries, 0, 2);
        int normalizedTimeout = Math.Clamp(pageTimeoutSeconds ?? (int)DefaultPageTimeout.TotalSeconds, 15, 600);
        int normalizedMaxParallelPages = Math.Clamp(maxParallelPages ?? DefaultMaxParallelPages, 1, 4);
        double normalizedConfidence = Math.Clamp(
            lowConfidenceThreshold ?? DefaultLowConfidenceThreshold,
            0.01d,
            0.99d);

        return new OcrPipelineOptions(
            normalizedLanguage,
            normalizedRetries,
            TimeSpan.FromSeconds(normalizedTimeout),
            normalizedConfidence,
            normalizedMaxParallelPages,
            OcrSettings.Normalize(settings ?? OcrSettings.Default));
    }
}
