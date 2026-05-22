namespace OnlyRag.Core;

public sealed record PerformanceSettings(
    int MaxParallelJobs,
    int MaxOcrParallelPages,
    int EmbeddingBatchSize,
    int TranslationBatchSize,
    int MaxContextChunks,
    int RequestTimeoutSeconds,
    bool EnableLowResourceMode,
    string Profile = PerformanceProfileNames.Auto,
    string EffectiveProfile = PerformanceProfileNames.Eco);
