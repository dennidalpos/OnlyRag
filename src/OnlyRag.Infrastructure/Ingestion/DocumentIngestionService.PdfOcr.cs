using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using UglyToad.PdfPig;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed partial class DocumentIngestionService
{
    private const string OcrMaxParallelPagesSettingKey = "performance.maxOcrParallelPages";

    private async Task<OcrPipelineOptions> LoadOcrOptionsAsync(
        string? languageOverride,
        CancellationToken cancellationToken)
    {
        string? maxParallelPagesValue = await settings.GetValueAsync(OcrMaxParallelPagesSettingKey, cancellationToken);
        OcrProcessingSettings processingSettings = await ocrProcessingSettingsStore.GetAsync(cancellationToken);
        OcrSettings ocrSettings = await ocrSettingsStore.GetAsync(cancellationToken);

        return OcrPipelineOptions.Normalize(
            string.IsNullOrWhiteSpace(languageOverride) ? processingSettings.Language : languageOverride,
            processingSettings.MaxRetries,
            processingSettings.PageTimeoutSeconds,
            processingSettings.LowConfidenceThreshold,
            int.TryParse(maxParallelPagesValue, out int maxParallelPages) ? maxParallelPages : null,
            ocrSettings);
    }

    private async Task<DocumentIngestionResult> IngestPdfAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        PdfDocument pdf;
        try
        {
            pdf = PdfDocument.Open(document.OriginalPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Impossibile aprire il file PDF. Il file potrebbe essere cifrato, danneggiato o in un formato non supportato. Dettaglio: {ex.Message}", ex);
        }

        using (pdf)
        {

        int totalPages = pdf.NumberOfPages;
        int nextPage = Math.Max(1, checkpoint.NextBlock);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int processedPageCount = Math.Max(0, checkpoint.PageCount);

        for (int pageNumber = nextPage; pageNumber <= totalPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text;
            try
            {
                text = forceOcr ? string.Empty : pdf.GetPage(pageNumber).Text.Trim();
            }
            catch (Exception)
            {
                text = string.Empty;
            }
            IngestedDocumentPage page = string.IsNullOrWhiteSpace(text)
                ? await RunOcrForPageAsync(document, "pdf", pageNumber, totalPages, nextChunkOrdinal, forceOcr, ocrLanguage, saveProgressAsync, cancellationToken)
                : new IngestedDocumentPage(pageNumber, text);

            IReadOnlyList<IngestedDocumentChunk> chunks = string.IsNullOrWhiteSpace(page.Text)
                ? []
                : chunker.CreateChunks(page.Text, pageNumber, pageNumber, nextChunkOrdinal, options);

            await documents.SaveIngestedPageAsync(
                document.Id,
                page,
                chunks,
                totalPages,
                cancellationToken);

            nextChunkOrdinal += chunks.Count;
            chunkCount = nextChunkOrdinal;
            processedPageCount = pageNumber;
            DocumentIngestionCheckpoint savedCheckpoint = checkpoint with
            {
                NextBlock = pageNumber + 1,
                PageCount = totalPages,
                NextChunkOrdinal = nextChunkOrdinal
            };
            await saveProgressAsync(
                new DocumentIngestionProgress(
                    CalculateProgress(pageNumber, totalPages),
                    $"PDF pagina {pageNumber}/{totalPages}",
                    savedCheckpoint),
                cancellationToken);
        }

        if (processedPageCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException("Il PDF non contiene testo estraibile e l'OCR non ha prodotto testo utilizzabile.");
        }

        return new DocumentIngestionResult(totalPages, chunkCount);

        } // end using (pdf)
    }

    private async Task<DocumentIngestionResult> IngestImageAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr,
        string? ocrLanguage,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        int nextPage = Math.Max(1, checkpoint.NextBlock);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        if (nextPage > 1)
        {
            return new DocumentIngestionResult(1, chunkCount);
        }

        IngestedDocumentPage page = await RunOcrForPageAsync(
            document,
            "image",
            1,
            1,
            nextChunkOrdinal,
            forceOcr,
            ocrLanguage,
            saveProgressAsync,
            cancellationToken);
        IReadOnlyList<IngestedDocumentChunk> chunks = string.IsNullOrWhiteSpace(page.Text)
            ? []
            : chunker.CreateChunks(page.Text, 1, 1, nextChunkOrdinal, options);

        await documents.SaveIngestedPageAsync(document.Id, page, chunks, 1, cancellationToken);
        chunkCount += chunks.Count;
        await saveProgressAsync(
            new DocumentIngestionProgress(
                99,
                "OCR immagine 1/1",
                checkpoint with
                {
                    NextBlock = 2,
                    PageCount = 1,
                    NextChunkOrdinal = chunkCount,
                    Mode = "ocr-image"
                }),
            cancellationToken);

        if (chunkCount == 0)
        {
            throw new InvalidOperationException("L'immagine non ha prodotto testo OCR utilizzabile.");
        }

        return new DocumentIngestionResult(1, chunkCount);
    }

    private async Task<IngestedDocumentPage> RunOcrForPageAsync(
        ImportedDocument document,
        string sourceKind,
        int pageNumber,
        int totalPages,
        int nextChunkOrdinal,
        bool forceOcr,
        string? ocrLanguage,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        OcrEngineAvailability availability = await ocrEngine.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsConfigured)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(availability.Message)
                    || availability.Message.Contains("Bootstrap", StringComparison.OrdinalIgnoreCase)
                    ? "PaddleOCR non configurato. Apri Impostazioni > Diagnostica e usa Configura OCR."
                    : availability.Message);
        }

        OcrPipelineOptions ocrOptions = await LoadOcrOptionsAsync(ocrLanguage, cancellationToken);
        string outputDirectory = descriptor?.Paths.DocumentRendersDirectory
            ?? Path.Combine(Path.GetTempPath(), "OnlyRag", "ocr-renders");
        OcrPagePreparation preparation = await ocrEngine.PreparePageAsync(
            new OcrPagePreparationRequest(
                document.OriginalPath,
                sourceKind,
                pageNumber,
                outputDirectory,
                ocrEngine.PreprocessVersion,
                ocrOptions.Settings),
            cancellationToken);

        string cacheKey = OcrCacheKey.Create(
            preparation.PageHash,
            ocrEngine.EngineName,
            availability.EngineVersion,
            ocrOptions.Language,
            ocrEngine.PreprocessVersion,
            ocrOptions.Settings.ToCacheSignature());

        if (!forceOcr && ocrCache is not null)
        {
            OcrCacheEntry? cached = await ocrCache.GetAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return new IngestedDocumentPage(
                    pageNumber,
                    cached.Text,
                    preparation.PreparedImagePath,
                    cacheKey,
                    "Cached",
                    cached.EngineName,
                    cached.Language,
                    cached.Confidence,
                    cached.BoxesJson,
                    OcrError: null);
            }
        }

        await saveProgressAsync(
            new DocumentIngestionProgress(
                CalculateProgress(pageNumber - 1, totalPages),
                $"OCR pagina {pageNumber}/{totalPages}",
                new DocumentIngestionCheckpoint(1, document.Id, pageNumber, totalPages, nextChunkOrdinal, "ocr")),
            cancellationToken);

        OcrPageResult result = await ocrRetryPolicy.ExecuteAsync(
            token => ocrEngine.RecognizeAsync(
                new OcrRecognitionRequest(preparation.PreparedImagePath, ocrOptions.Language, ocrOptions.Settings),
                token),
            ocrOptions,
            cancellationToken);

        string boxesJson = JsonSerializer.Serialize(result.Boxes);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (ocrCache is not null)
        {
            await ocrCache.UpsertAsync(
                new OcrCacheEntry(
                    cacheKey,
                    preparation.PageHash,
                    result.EngineName,
                    availability.EngineVersion,
                    result.Language,
                    ocrEngine.PreprocessVersion,
                    result.Text,
                    boxesJson,
                    result.AverageConfidence,
                    now,
                    now),
                cancellationToken);
        }

        string status = result.AverageConfidence is not null
            && result.AverageConfidence < ocrOptions.LowConfidenceThreshold
                ? "LowConfidence"
                : "Complete";

        return new IngestedDocumentPage(
            pageNumber,
            result.Text,
            preparation.PreparedImagePath,
            cacheKey,
            status,
            result.EngineName,
            result.Language,
            result.AverageConfidence,
            boxesJson,
            OcrError: null);
    }
}
