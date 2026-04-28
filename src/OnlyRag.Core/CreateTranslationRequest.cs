namespace OnlyRag.Core;

public sealed record CreateTranslationRequest(
    long DocumentId,
    string TargetLanguage,
    string Model);
