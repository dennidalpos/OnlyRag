using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed record CreateDocumentRecordRequest(
    string DocumentUid,
    string OriginalFileName,
    string OriginalPath,
    string Sha256,
    string MimeType,
    string FileExtension,
    long FileSizeBytes,
    DocumentStatus Status,
    int PageCount,
    string? CurrentJobId,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
