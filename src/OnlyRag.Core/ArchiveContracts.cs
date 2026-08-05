namespace OnlyRag.Core;

public enum ArchiveManifestStatus
{
    Pending,
    Processing,
    Indexed,
    Skipped,
    Duplicate,
    Failed
}

public sealed record ArchiveManifestEntry(
    long Id,
    long ContainerDocumentId,
    int EntryIndex,
    string RelativePath,
    long DeclaredSizeBytes,
    long UncompressedSizeBytes,
    string? ContentSha256,
    ArchiveManifestStatus Status,
    string? Error,
    int PageCount,
    int ChunkCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
