using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed partial class DocumentIngestionService : IDocumentIngestionService
{
    private readonly IDocumentRepository documents;
    private readonly ISettingsRepository settings;
    private readonly DocumentTextChunker chunker;
    private readonly OfficeOpenXmlTextExtractor officeExtractor;
    private readonly IOcrEngine ocrEngine;
    private readonly IOcrCacheRepository? ocrCache;
    private readonly OcrRetryPolicy ocrRetryPolicy;
    private readonly OcrSettingsStore ocrSettingsStore;
    private readonly IngestionSettingsStore ingestionSettingsStore;
    private readonly OcrProcessingSettingsStore ocrProcessingSettingsStore;
    private readonly LocalSqliteStoreDescriptor? descriptor;

    public DocumentIngestionService(
        IDocumentRepository documents,
        ISettingsRepository settings,
        DocumentTextChunker chunker,
        OfficeOpenXmlTextExtractor officeExtractor,
        IOcrEngine? ocrEngine = null,
        IOcrCacheRepository? ocrCache = null,
        OcrRetryPolicy? ocrRetryPolicy = null,
        LocalSqliteStoreDescriptor? descriptor = null,
        OcrSettingsStore? ocrSettingsStore = null,
        IngestionSettingsStore? ingestionSettingsStore = null,
        OcrProcessingSettingsStore? ocrProcessingSettingsStore = null)
    {
        this.documents = documents;
        this.settings = settings;
        this.chunker = chunker;
        this.officeExtractor = officeExtractor;
        this.ocrEngine = ocrEngine ?? new UnavailableOcrEngine();
        this.ocrCache = ocrCache;
        this.ocrRetryPolicy = ocrRetryPolicy ?? new OcrRetryPolicy();
        this.ocrSettingsStore = ocrSettingsStore ?? new OcrSettingsStore(settings);
        this.ingestionSettingsStore = ingestionSettingsStore ?? new IngestionSettingsStore(settings);
        this.ocrProcessingSettingsStore = ocrProcessingSettingsStore ?? new OcrProcessingSettingsStore(settings);
        this.descriptor = descriptor;
    }

    public async Task<DocumentIngestionResult> IngestAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint? checkpoint,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(saveProgressAsync);

        DocumentIngestionOptions options = await LoadOptionsAsync(cancellationToken);
        string extension = (document.FileExtension ?? Path.GetExtension(document.OriginalFileName)).ToLowerInvariant();

        if (checkpoint is null || checkpoint.DocumentId != document.Id)
        {
            await documents.ClearIngestionAsync(document.Id, cancellationToken);
            checkpoint = new DocumentIngestionCheckpoint(1, document.Id, 1, 0, 0, extension);
        }

        return extension switch
        {
            ".txt" or ".md" or ".markdown" or ".csv" => await IngestTextFileAsync(document, checkpoint, options, saveProgressAsync, cancellationToken),
            ".pdf" => await IngestPdfAsync(document, checkpoint, options, saveProgressAsync, forceOcr, ocrLanguage, cancellationToken),
            ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".gif" or ".webp" => await IngestImageAsync(document, checkpoint, options, saveProgressAsync, forceOcr, ocrLanguage, cancellationToken),
            ".docx" or ".xlsx" or ".pptx" => await IngestOfficeOpenXmlAsync(document, checkpoint, options, extension, saveProgressAsync, cancellationToken),
            _ => throw new InvalidOperationException($"Formato documento non supportato per ingestion iniziale: {extension}.")
        };
    }

    private async Task<DocumentIngestionOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        IngestionSettings ingestionSettings = await ingestionSettingsStore.GetAsync(cancellationToken);
        return IngestionSettingsStore.ToOptions(ingestionSettings);
    }

    public static DocumentIngestionCheckpoint? ReadCheckpoint(string checkpointJson)
    {
        if (string.IsNullOrWhiteSpace(checkpointJson) || checkpointJson == "{}")
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DocumentIngestionCheckpoint>(checkpointJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
