namespace OnlyRag.Core;

public sealed record DocumentPreAnalysis(
    string FileName,
    string FileExtension,
    string MimeType,
    long FileSizeBytes,
    bool IsOcrCandidate,
    int? EstimatedPageCount);
