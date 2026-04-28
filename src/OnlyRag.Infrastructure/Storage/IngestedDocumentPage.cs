namespace OnlyRag.Infrastructure.Storage;

public sealed record IngestedDocumentPage(
    int PageNumber,
    string Text,
    string? RenderPath = null,
    string? OcrCacheKey = null,
    string? OcrStatus = null,
    string? OcrEngine = null,
    string? OcrLanguage = null,
    double? OcrConfidence = null,
    string? OcrBoxesJson = null,
    string? OcrError = null);
