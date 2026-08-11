using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface IArchiveManifestRepository
{
    Task<ArchiveManifestEntry?> GetAsync(
        long containerDocumentId,
        int entryIndex,
        CancellationToken cancellationToken = default);

    Task<ArchiveManifestEntry?> FindByPathAsync(
        long containerDocumentId,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task<ArchiveManifestEntry> UpsertPendingAsync(
        long containerDocumentId,
        int entryIndex,
        string relativePath,
        long declaredSizeBytes,
        CancellationToken cancellationToken = default);

    Task<ArchiveManifestEntry?> UpdateAsync(
        long containerDocumentId,
        int entryIndex,
        ArchiveManifestStatus status,
        long? uncompressedSizeBytes = null,
        string? contentSha256 = null,
        string? error = null,
        int? pageCount = null,
        int? chunkCount = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveManifestEntry>> ListAsync(
        long containerDocumentId,
        CancellationToken cancellationToken = default);
}
