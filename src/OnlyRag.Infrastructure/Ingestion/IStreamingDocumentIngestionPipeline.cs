using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Ingestion;

public interface IStreamingDocumentIngestionPipeline
{
    Task<DocumentIngestionResult> ProcessStreamAsync(
        ImportedDocument document,
        IAsyncEnumerable<ParsedPageBlock> pageBlockStream,
        DocumentIngestionOptions ingestionOptions,
        StreamingIngestionOptions streamingOptions,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        string? embeddingModel = null,
        CancellationToken cancellationToken = default);
}
