namespace OnlyRag.Core;

public sealed record OllamaSettings(
    string OllamaBaseUrl,
    string? DefaultChatModel,
    string? DefaultEmbeddingModel,
    string? DefaultTranslationModel,
    int RequestTimeoutSeconds,
    int EmbeddingBatchSize,
    int? EmbeddingNumCtx = null,
    int? ChatNumCtx = null,
    int? TranslationNumCtx = null);
