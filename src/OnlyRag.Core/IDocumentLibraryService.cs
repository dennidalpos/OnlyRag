namespace OnlyRag.Core;

public static class DocumentJobTypes
{
    public const string DocumentIngestionJobType = "document-ingestion";
}

public interface IDocumentLibraryService
{
    Task<IReadOnlyList<ImportedDocument>> ListAsync(CancellationToken cancellationToken = default);

    Task<ImportedDocument?> GetAsync(long id, CancellationToken cancellationToken = default);

    Task<DocumentImportResult> ImportAsync(
        Stream source,
        string fileName,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default);

    Task<ImportedDocument?> QueueForIndexingAsync(
        long id,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default);

    Task<ImportedDocument?> SetStatusAsync(
        long id,
        DocumentStatus status,
        string? currentJobId,
        string? lastError,
        CancellationToken cancellationToken = default);

    Task<ImportedDocument?> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
