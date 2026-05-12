namespace OnlyRag.Core;

public sealed record DocumentPreviewResponse(
    long DocumentId,
    string OriginalFileName,
    string? MimeType,
    string? FileExtension,
    long FileSizeBytes,
    int PageCount,
    int ChunkCount,
    string Status,
    int PageStart,
    int PageSize,
    int ReturnedPageCount,
    IReadOnlyList<DocumentPageInfo> Pages);
