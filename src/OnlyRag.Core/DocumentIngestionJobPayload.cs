namespace OnlyRag.Core;

public sealed record DocumentIngestionJobPayload(
    long DocumentId,
    string DocumentUid,
    string OriginalFileName,
    string Sha256,
    bool ForceOcr = false,
    string? OcrLanguage = null);
