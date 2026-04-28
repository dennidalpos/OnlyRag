namespace OnlyRag.Core;

public sealed record DocumentPageInfo(
    int PageNumber,
    string? TextContent,
    string? OcrStatus,
    string? OcrEngine,
    double? OcrConfidence,
    string? OcrError);
