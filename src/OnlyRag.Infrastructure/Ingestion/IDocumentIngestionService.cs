using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ingestion;

public interface IDocumentIngestionService
{
    Task<DocumentIngestionResult> IngestAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint? checkpoint,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default);
}
