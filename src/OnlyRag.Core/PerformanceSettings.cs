namespace OnlyRag.Core;

public sealed record PerformanceSettings(
    int MaxParallelJobs,
    int MaxOcrParallelPages,
    int EmbeddingBatchSize,
    int TranslationBatchSize,
    int MaxContextChunks,
    int RequestTimeoutSeconds,
    bool EnableLowResourceMode);
