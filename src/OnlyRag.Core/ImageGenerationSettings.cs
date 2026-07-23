namespace OnlyRag.Core;

public sealed record ImageGenerationSettings(
    string SelectedModelId,
    int RequestTimeoutSeconds,
    bool PreferGpu);

public sealed record ImagePromptTranslationRequest(
    string Prompt,
    string SourceLanguage);

public sealed record ImagePromptTranslationResponse(
    string OriginalPrompt,
    string TranslatedPrompt,
    string TargetLanguage);
