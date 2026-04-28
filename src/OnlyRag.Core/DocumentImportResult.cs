namespace OnlyRag.Core;

public sealed record DocumentImportResult(
    ImportedDocument Document,
    bool Deduplicated,
    string Message);
