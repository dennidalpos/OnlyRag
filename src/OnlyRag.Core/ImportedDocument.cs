namespace OnlyRag.Core;

public sealed record ImportedDocument(
    long Id,
    string DocumentUid,
    string OriginalFileName,
    string OriginalPath,
    string? Sha256,
    string? MimeType,
    string? FileExtension,
    long FileSizeBytes,
    DocumentStatus Status,
    int PageCount,
    int ChunkCount,
    string? CurrentJobId,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
