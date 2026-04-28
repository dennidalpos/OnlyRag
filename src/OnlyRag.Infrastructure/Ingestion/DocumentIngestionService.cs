using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using UglyToad.PdfPig;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private const int TextBlockTargetCharacters = 64 * 1024;
    private const string ChunkSizeSettingKey = "ingestion.chunkSizeTokens";
    private const string OverlapSettingKey = "ingestion.overlapTokens";
    private const string OcrLanguageSettingKey = "ocr.language";
    private const string OcrMaxRetriesSettingKey = "ocr.maxRetries";
    private const string OcrPageTimeoutSettingKey = "ocr.pageTimeoutSeconds";
    private const string OcrLowConfidenceSettingKey = "ocr.lowConfidenceThreshold";
    private const string OcrMaxParallelPagesSettingKey = "performance.maxOcrParallelPages";

    private readonly IDocumentRepository documents;
    private readonly ISettingsRepository settings;
    private readonly DocumentTextChunker chunker;
    private readonly OfficeOpenXmlTextExtractor officeExtractor;
    private readonly IOfficeConversionService officeConversion;
    private readonly IOcrEngine ocrEngine;
    private readonly IOcrCacheRepository? ocrCache;
    private readonly OcrRetryPolicy ocrRetryPolicy;
    private readonly OcrSettingsStore ocrSettingsStore;
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
        IOfficeConversionService? officeConversion = null,
        OcrSettingsStore? ocrSettingsStore = null)
    {
        this.documents = documents;
        this.settings = settings;
        this.chunker = chunker;
        this.officeExtractor = officeExtractor;
        this.officeConversion = officeConversion ?? new UnavailableOfficeConversionService();
        this.ocrEngine = ocrEngine ?? new UnavailableOcrEngine();
        this.ocrCache = ocrCache;
        this.ocrRetryPolicy = ocrRetryPolicy ?? new OcrRetryPolicy();
        this.ocrSettingsStore = ocrSettingsStore ?? new OcrSettingsStore(settings);
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
            ".doc" or ".xls" or ".ppt" => await IngestOfficeThroughPdfAsync(document, checkpoint, options, extension, saveProgressAsync, cancellationToken),
            _ => throw new InvalidOperationException($"Formato documento non supportato per ingestion iniziale: {extension}.")
        };
    }

    private async Task<DocumentIngestionOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        string? chunkSizeValue = await settings.GetValueAsync(ChunkSizeSettingKey, cancellationToken);
        string? overlapValue = await settings.GetValueAsync(OverlapSettingKey, cancellationToken);

        int chunkSize = int.TryParse(chunkSizeValue, out int parsedChunkSize)
            ? parsedChunkSize
            : DocumentIngestionOptions.DefaultChunkSizeTokens;
        int overlap = int.TryParse(overlapValue, out int parsedOverlap)
            ? parsedOverlap
            : DocumentIngestionOptions.DefaultOverlapTokens;

        return DocumentIngestionOptions.Normalize(chunkSize, overlap);
    }

    private async Task<OcrPipelineOptions> LoadOcrOptionsAsync(
        string? languageOverride,
        CancellationToken cancellationToken)
    {
        string? language = string.IsNullOrWhiteSpace(languageOverride)
            ? await settings.GetValueAsync(OcrLanguageSettingKey, cancellationToken)
            : languageOverride;
        string? retriesValue = await settings.GetValueAsync(OcrMaxRetriesSettingKey, cancellationToken);
        string? timeoutValue = await settings.GetValueAsync(OcrPageTimeoutSettingKey, cancellationToken);
        string? lowConfidenceValue = await settings.GetValueAsync(OcrLowConfidenceSettingKey, cancellationToken);
        string? maxParallelPagesValue = await settings.GetValueAsync(OcrMaxParallelPagesSettingKey, cancellationToken);
        OcrSettings ocrSettings = await ocrSettingsStore.GetAsync(cancellationToken);

        return OcrPipelineOptions.Normalize(
            language,
            int.TryParse(retriesValue, out int retries) ? retries : null,
            int.TryParse(timeoutValue, out int timeout) ? timeout : null,
            double.TryParse(lowConfidenceValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double confidence)
                ? confidence
                : null,
            int.TryParse(maxParallelPagesValue, out int maxParallelPages) ? maxParallelPages : null,
            ocrSettings);
    }

    private async Task<DocumentIngestionResult> IngestTextFileAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        FileInfo file = new(document.OriginalPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        int nextBlock = Math.Max(1, checkpoint.NextBlock);
        int pageCount = Math.Max(0, checkpoint.PageCount);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int blockNumber = 0;

        await using FileStream stream = new(
            document.OriginalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? block = await ReadNextTextBlockAsync(reader, cancellationToken);
            if (block is null)
            {
                break;
            }

            blockNumber++;
            if (blockNumber < nextBlock)
            {
                continue;
            }

            string normalizedBlock = block.Trim();
            if (normalizedBlock.Length == 0)
            {
                pageCount = blockNumber;
                DocumentIngestionCheckpoint emptyCheckpoint = checkpoint with
                {
                    NextBlock = blockNumber + 1,
                    PageCount = pageCount,
                    NextChunkOrdinal = nextChunkOrdinal
                };
                await saveProgressAsync(CreateProgress(document, file.Length, stream.Position, emptyCheckpoint), cancellationToken);
                continue;
            }

            IReadOnlyList<IngestedDocumentChunk> chunks = chunker.CreateChunks(
                normalizedBlock,
                blockNumber,
                blockNumber,
                nextChunkOrdinal,
                options);
            await documents.SaveIngestedPageAsync(
                document.Id,
                new IngestedDocumentPage(blockNumber, normalizedBlock),
                chunks,
                blockNumber,
                cancellationToken);

            nextChunkOrdinal += chunks.Count;
            chunkCount = nextChunkOrdinal;
            pageCount = blockNumber;
            DocumentIngestionCheckpoint savedCheckpoint = checkpoint with
            {
                NextBlock = blockNumber + 1,
                PageCount = pageCount,
                NextChunkOrdinal = nextChunkOrdinal
            };
            await saveProgressAsync(CreateProgress(document, file.Length, stream.Position, savedCheckpoint), cancellationToken);
        }

        if (pageCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException("Il documento testuale non contiene testo estraibile.");
        }

        return new DocumentIngestionResult(pageCount, chunkCount);
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
                ? await RunOcrForPageAsync(document, "pdf", pageNumber, totalPages, forceOcr, ocrLanguage, saveProgressAsync, cancellationToken)
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
        bool forceOcr,
        string? ocrLanguage,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        OcrEngineAvailability availability = await ocrEngine.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsConfigured)
        {
            throw new InvalidOperationException(
                availability.Message
                ?? "PaddleOCR non configurato. Esegui scripts\\Bootstrap-Prerequisites.ps1 oppure imposta ONLYRAG_OCR_PYTHON.");
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
                new DocumentIngestionCheckpoint(1, document.Id, pageNumber, totalPages, 0, "ocr")),
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

    private async Task<DocumentIngestionResult> IngestOfficeOpenXmlAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        string extension,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        IReadOnlyList<OfficeOpenXmlTextUnit> units;
        try
        {
            units = officeExtractor.Extract(document.OriginalPath, extension);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or IOException)
        {
            return await IngestOfficeThroughPdfAsync(
                document,
                checkpoint with { Mode = $"{extension}-fallback" },
                options,
                extension,
                saveProgressAsync,
                cancellationToken);
        }

        int totalUnits = units.Count;
        int nextUnit = Math.Max(1, checkpoint.NextBlock);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int processedUnitCount = Math.Max(0, checkpoint.PageCount);

        for (int unitIndex = nextUnit - 1; unitIndex < units.Count; unitIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            OfficeOpenXmlTextUnit unit = units[unitIndex];
            string text = unit.Text.Trim();
            IReadOnlyList<IngestedDocumentChunk> chunks = string.IsNullOrWhiteSpace(text)
                ? []
                : chunker.CreateChunks(text, unit.UnitNumber, unit.UnitNumber, nextChunkOrdinal, options);

            await documents.SaveIngestedPageAsync(
                document.Id,
                new IngestedDocumentPage(unit.UnitNumber, text),
                chunks,
                totalUnits,
                cancellationToken);

            nextChunkOrdinal += chunks.Count;
            chunkCount = nextChunkOrdinal;
            processedUnitCount = unit.UnitNumber;
            DocumentIngestionCheckpoint savedCheckpoint = checkpoint with
            {
                NextBlock = unit.UnitNumber + 1,
                PageCount = totalUnits,
                NextChunkOrdinal = nextChunkOrdinal,
                Mode = extension
            };
            await saveProgressAsync(
                new DocumentIngestionProgress(
                    CalculateProgress(unit.UnitNumber, totalUnits),
                    $"{DescribeOfficeUnit(extension)} {unit.UnitNumber}/{totalUnits}",
                    savedCheckpoint),
                cancellationToken);
        }

        if (processedUnitCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException($"{DescribeOfficeFormat(extension)} non contiene testo estraibile.");
        }

        return new DocumentIngestionResult(totalUnits, chunkCount);
    }

    private async Task<DocumentIngestionResult> IngestOfficeThroughPdfAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        string extension,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale documento non trovato.", document.OriginalPath);
        }

        await saveProgressAsync(
            new DocumentIngestionProgress(
                Math.Max(1, CalculateProgress(Math.Max(0, checkpoint.NextBlock - 1), Math.Max(1, checkpoint.PageCount))),
                $"Conversione {DescribeOfficeFormat(extension)} richiesta",
                checkpoint with { Mode = $"{extension}-conversion" }),
            cancellationToken);

        OfficeConversionResult? conversion = null;
        try
        {
            conversion = await officeConversion.ConvertToPdfAsync(
                new OfficeConversionRequest(
                    document.Id,
                    document.OriginalPath,
                    document.OriginalFileName,
                    extension),
                cancellationToken);

            ImportedDocument convertedDocument = document with
            {
                OriginalFileName = Path.ChangeExtension(document.OriginalFileName, ".pdf"),
                OriginalPath = conversion.PdfPath,
                MimeType = "application/pdf",
                FileExtension = ".pdf"
            };

            return await IngestPdfAsync(
                convertedDocument,
                checkpoint with { Mode = $"{extension}-pdf" },
                options,
                saveProgressAsync,
                forceOcr: false,
                ocrLanguage: null,
                cancellationToken);
        }
        catch (OfficeConversionUnavailableException)
        {
            throw;
        }
        catch (OfficeConversionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new OfficeConversionException(
                $"Conversione {DescribeOfficeFormat(extension)} non riuscita: {ex.Message}",
                ex);
        }
        finally
        {
            if (conversion is not null && Directory.Exists(conversion.TemporaryDirectory))
            {
                try
                {
                    Directory.Delete(conversion.TemporaryDirectory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Il job ha gia prodotto un risultato; il cleanup residuo verra ritentato al prossimo avvio o manualmente.
                }
            }
        }
    }

    private static async Task<string?> ReadNextTextBlockAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        StringBuilder builder = new();

        while (true)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
            if (builder.Length >= TextBlockTargetCharacters && string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            if (builder.Length >= TextBlockTargetCharacters * 2)
            {
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static DocumentIngestionProgress CreateProgress(
        ImportedDocument document,
        long fileLength,
        long streamPosition,
        DocumentIngestionCheckpoint checkpoint)
    {
        int progress = fileLength <= 0
            ? 0
            : Math.Clamp((int)Math.Round(streamPosition * 100d / fileLength), 0, 99);
        return new DocumentIngestionProgress(
            progress,
            $"Blocco testo {checkpoint.NextBlock - 1}",
            checkpoint with { Mode = document.FileExtension ?? checkpoint.Mode });
    }

    private static int CalculateProgress(int completed, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)Math.Round(completed * 100d / total), 0, 99);
    }

    private static string DescribeOfficeFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "DOCX",
            ".xlsx" => "XLSX",
            ".pptx" => "PPTX",
            ".doc" => "DOC legacy",
            ".xls" => "XLS legacy",
            ".ppt" => "PPT legacy",
            _ => "Office Open XML"
        };
    }

    private static string DescribeOfficeUnit(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".docx" => "DOCX sezione logica",
            ".xlsx" => "XLSX foglio",
            ".pptx" => "PPTX slide",
            _ => "Unita documento"
        };
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
