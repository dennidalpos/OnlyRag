using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface IDocumentRepository
{
    Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default);

    Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<ImportedDocument?> FindBySha256Async(string sha256, CancellationToken cancellationToken = default);

    Task<ImportedDocument> CreateAsync(CreateDocumentRecordRequest request, CancellationToken cancellationToken = default);

    Task<ImportedDocument?> UpdateStatusAsync(
        long id,
        DocumentStatus status,
        string? currentJobId,
        string? lastError,
        CancellationToken cancellationToken = default);

    Task ClearIngestionAsync(long documentId, CancellationToken cancellationToken = default);

    Task SaveIngestedPageAsync(
        long documentId,
        IngestedDocumentPage page,
        IReadOnlyList<IngestedDocumentChunk> chunks,
        int pageCount,
        CancellationToken cancellationToken = default);

    Task<DocumentOcrStatusResponse> GetOcrStatusAsync(
        long documentId,
        string? currentJobId,
        string? currentStep,
        CancellationToken cancellationToken = default);

    Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentPageInfo>> GetPagesAsync(long documentId, CancellationToken cancellationToken = default);
}
