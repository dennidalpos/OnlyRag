namespace OnlyRag.Core;

public sealed record ImageGenerationSettings(
    string SelectedModelId,
    int RequestTimeoutSeconds,
    bool PreferGpu,
    string ActiveExecutionProvider);
