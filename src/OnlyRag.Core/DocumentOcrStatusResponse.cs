namespace OnlyRag.Core;

public sealed record DocumentOcrStatusResponse(
    long DocumentId,
    string State,
    int PageCount,
    int OcrPageCount,
    int CurrentPage,
    int TotalPages,
    double? AverageConfidence,
    string? CurrentJobId,
    string? CurrentStep,
    string? EngineName,
    string? LastError);
