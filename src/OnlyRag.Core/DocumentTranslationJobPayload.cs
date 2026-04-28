namespace OnlyRag.Core;

public sealed record DocumentTranslationJobPayload(
    long TranslationId,
    long DocumentId,
    string TargetLanguage,
    string Model);
