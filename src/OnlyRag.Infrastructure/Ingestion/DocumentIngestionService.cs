using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed class DocumentIngestionService : IDocumentIngestionService
{
    private const int TextBlockTargetCharacters = 64 * 1024;

    private readonly IDocumentRepository documents;
    private readonly ISettingsRepository settings;
    private readonly DocumentTextChunker chunker;
    private readonly OfficeOpenXmlTextExtractor officeExtractor;
    private readonly IOcrEngine ocrEngine;
    private readonly IOcrCacheRepository? ocrCache;
    private readonly OcrRetryPolicy ocrRetryPolicy;
    private readonly OcrSettingsStore ocrSettingsStore;
    private readonly IngestionSettingsStore ingestionSettingsStore;
    private readonly ArchiveExtractionService archiveExtractionService;
    private readonly IArchiveManifestRepository? archiveManifestRepository;
    private readonly LocalSqliteStoreDescriptor? descriptor;
    private readonly Retrieval.Graph.IEntityGraphExtractor? graphExtractor;
    private readonly Retrieval.Graph.IGraphRetrievalService? graphService;

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
        ArchiveExtractionService? archiveExtractionService = null,
        IArchiveManifestRepository? archiveManifestRepository = null,
        Retrieval.Graph.IEntityGraphExtractor? graphExtractor = null,
        Retrieval.Graph.IGraphRetrievalService? graphService = null)
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
        this.archiveExtractionService = archiveExtractionService ?? new ArchiveExtractionService();
        this.archiveManifestRepository = archiveManifestRepository;
        this.descriptor = descriptor;
        this.graphExtractor = graphExtractor;
        this.graphService = graphService;
    }

    public Task<DocumentIngestionResult> IngestAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint? checkpoint,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default)
    {
        return IngestAsync(
            document,
            checkpoint,
            DocumentIngestionOptions.Default,
            saveProgressAsync,
            forceOcr,
            ocrLanguage,
            cancellationToken);
    }

    public async Task<DocumentIngestionResult> IngestAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint? checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        bool forceOcr = false,
        string? ocrLanguage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(saveProgressAsync);

        IngestionSettings persistedIngestionSettings = await ingestionSettingsStore.GetAsync(cancellationToken);
        DocumentIngestionOptions effectiveOptions = DocumentIngestionOptions.Normalize(
            options.ChunkSizeTokens > 0 ? options.ChunkSizeTokens : persistedIngestionSettings.ChunkSizeTokens,
            options.OverlapTokens >= 0 ? options.OverlapTokens : persistedIngestionSettings.OverlapTokens);

        DocumentIngestionCheckpoint currentCheckpoint = checkpoint ?? new DocumentIngestionCheckpoint(1, document.Id, 1, 0, 0, "initial");
        string extension = document.FileExtension?.ToLowerInvariant() ?? string.Empty;

        return extension switch
        {
            ".txt" or ".md" or ".markdown" or ".csv" => await IngestTextFileAsync(document, currentCheckpoint, effectiveOptions, saveProgressAsync, cancellationToken),
            ".docx" or ".xlsx" or ".pptx" => await IngestOfficeOpenXmlAsync(document, currentCheckpoint, effectiveOptions, extension, saveProgressAsync, cancellationToken),
            ".pdf" => await IngestPdfAsync(document, currentCheckpoint, effectiveOptions, saveProgressAsync, forceOcr, ocrLanguage, cancellationToken),
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".webp" => await IngestImageAsync(document, currentCheckpoint, effectiveOptions, saveProgressAsync, forceOcr, ocrLanguage, cancellationToken),
            ".zip" or ".tar" or ".7z" => await IngestArchiveAsync(document, currentCheckpoint, effectiveOptions, saveProgressAsync, cancellationToken),
            _ => throw new InvalidOperationException($"Formato file non supportato dall'ingestion: {document.FileExtension}")
        };
    }

    private async Task<DocumentIngestionResult> IngestArchiveAsync(
        ImportedDocument document,
        DocumentIngestionCheckpoint checkpoint,
        DocumentIngestionOptions options,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(document.OriginalPath))
        {
            throw new FileNotFoundException("File originale archivio non trovato.", document.OriginalPath);
        }

        IngestionSettings ingestionSettings = await ingestionSettingsStore.GetAsync(cancellationToken);
        int nextEntry = Math.Max(1, checkpoint.NextBlock);
        int entryNumber = 0;
        int pageCount = Math.Max(0, checkpoint.PageCount);
        int chunkCount = Math.Max(0, checkpoint.NextChunkOrdinal);
        Dictionary<int, ArchiveEntryProcessingResult> processingResults = [];
        if (archiveManifestRepository is not null)
        {
            IReadOnlyList<ArchiveManifestEntry> existingManifest = await archiveManifestRepository.ListAsync(document.Id, cancellationToken);
            pageCount = Math.Max(
                pageCount,
                existingManifest
                    .Where(entry => entry.Status is ArchiveManifestStatus.Indexed)
                    .Sum(entry => entry.PageCount));
            chunkCount = Math.Max(
                chunkCount,
                existingManifest
                    .Where(entry => entry.Status is ArchiveManifestStatus.Indexed)
                    .Sum(entry => entry.ChunkCount));
        }

        await using FileStream archiveStream = new(
            document.OriginalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await archiveExtractionService.ExtractAsync(
            archiveStream,
            document.OriginalFileName,
            ingestionSettings.Archive,
            async (entry, content, token) =>
            {
                entryNumber++;
                ArchiveManifestEntry? manifest = archiveManifestRepository is null
                    ? null
                    : await archiveManifestRepository.UpsertPendingAsync(
                        document.Id,
                        entryNumber,
                        entry.RelativePath,
                        entry.Length,
                        token);
                ArchiveManifestEntry? firstEntryWithPath = archiveManifestRepository is null
                    ? null
                    : await archiveManifestRepository.FindByPathAsync(document.Id, entry.RelativePath, token);
                if (entryNumber < nextEntry)
                {
                    return;
                }

                if (manifest is not null
                    && manifest.EntryIndex == entryNumber
                    && manifest.Status is ArchiveManifestStatus.Indexed or ArchiveManifestStatus.Skipped or ArchiveManifestStatus.Duplicate)
                {
                    await SaveArchiveProgressAsync(
                        document,
                        entryNumber,
                        pageCount,
                        chunkCount,
                        $"Archivio elemento gia processato: {entry.RelativePath}",
                        saveProgressAsync,
                        token);
                    return;
                }

                if (manifest is not null
                    && firstEntryWithPath is not null
                    && firstEntryWithPath.EntryIndex != entryNumber
                    && firstEntryWithPath.Status is ArchiveManifestStatus.Indexed or ArchiveManifestStatus.Skipped or ArchiveManifestStatus.Duplicate)
                {
                    processingResults[entryNumber] = new ArchiveEntryProcessingResult(
                        ArchiveManifestStatus.Duplicate,
                        "Elemento duplicato nello stesso archivio.",
                        0,
                        0);
                    await SaveArchiveProgressAsync(
                        document,
                        entryNumber,
                        pageCount,
                        chunkCount,
                        $"Archivio elemento duplicato: {entry.RelativePath}",
                        saveProgressAsync,
                        token);
                    return;
                }

                if (archiveManifestRepository is not null)
                {
                    await archiveManifestRepository.UpdateAsync(
                        document.Id,
                        entryNumber,
                        ArchiveManifestStatus.Processing,
                        cancellationToken: token);
                }

                try
                {
                    IReadOnlyList<ArchiveEntryText> extracted = await ExtractArchiveEntryTextAsync(entry, content, token);
                    if (extracted.Count == 0)
                    {
                        processingResults[entryNumber] = new ArchiveEntryProcessingResult(
                            ArchiveManifestStatus.Skipped,
                            "Formato elemento non supportato o contenuto vuoto.",
                            0,
                            0);
                        await SaveArchiveProgressAsync(
                            document,
                            entryNumber,
                            pageCount,
                            chunkCount,
                            $"Archivio elemento ignorato: {entry.RelativePath}",
                            saveProgressAsync,
                            token);
                        return;
                    }

                    int entryPageCount = 0;
                    int entryChunkCount = 0;
                    foreach (ArchiveEntryText item in extracted)
                    {
                        token.ThrowIfCancellationRequested();
                        int pageNumber = ++pageCount;
                        entryPageCount++;
                        string text = $"[Archivio: {entry.RelativePath}{item.PageSuffix}]\n\n{item.Text}".Trim();
                        IReadOnlyList<IngestedDocumentChunk> chunks = chunker.CreateChunks(
                            text,
                            pageNumber,
                            pageNumber,
                            chunkCount,
                            options);
                        await documents.SaveIngestedPageAsync(
                            document.Id,
                            new IngestedDocumentPage(pageNumber, text),
                            chunks,
                            pageCount,
                            token);
                        await ExtractAndSaveGraphAsync(document.Id, chunks, token);
                        chunkCount += chunks.Count;
                        entryChunkCount += chunks.Count;
                    }

                    processingResults[entryNumber] = new ArchiveEntryProcessingResult(
                        ArchiveManifestStatus.Indexed,
                        null,
                        entryPageCount,
                        entryChunkCount);
                    await SaveArchiveProgressAsync(
                        document,
                        entryNumber,
                        pageCount,
                        chunkCount,
                        $"Archivio elemento {entryNumber}: {entry.RelativePath}",
                        saveProgressAsync,
                        token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    processingResults[entryNumber] = new ArchiveEntryProcessingResult(
                        ArchiveManifestStatus.Failed,
                        ex.Message,
                        0,
                        0);
                    if (archiveManifestRepository is not null)
                    {
                        await archiveManifestRepository.UpdateAsync(
                            document.Id,
                            entryNumber,
                            ArchiveManifestStatus.Failed,
                            error: ex.Message,
                            cancellationToken: token);
                    }

                    throw;
                }
            },
            async (entry, token) =>
            {
                if (archiveManifestRepository is null
                    || !processingResults.TryGetValue(entry.EntryIndex, out ArchiveEntryProcessingResult? result))
                {
                    return;
                }

                await archiveManifestRepository.UpdateAsync(
                    document.Id,
                    entry.EntryIndex,
                    result.Status,
                    entry.BytesRead,
                    entry.ContentSha256,
                    result.Error,
                    result.PageCount,
                    result.ChunkCount,
                    token);
            },
            cancellationToken);

        if (pageCount == 0 || chunkCount == 0)
        {
            throw new InvalidOperationException("L'archivio non contiene elementi testuali supportati.");
        }

        return new DocumentIngestionResult(pageCount, chunkCount);
    }

    private async Task<IReadOnlyList<ArchiveEntryText>> ExtractArchiveEntryTextAsync(
        ArchiveEntryContent entry,
        Stream content,
        CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(entry.RelativePath).ToLowerInvariant();
        if (extension is ".txt" or ".md" or ".markdown" or ".csv")
        {
            using StreamReader reader = new(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: true);
            string text = (await reader.ReadToEndAsync(cancellationToken)).Trim();
            return string.IsNullOrWhiteSpace(text) ? [] : [new ArchiveEntryText(text, string.Empty)];
        }

        if (extension is not ".docx" and not ".xlsx" and not ".pptx" and not ".pdf")
        {
            return [];
        }

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "OnlyRag", "archive-entries");
        Directory.CreateDirectory(temporaryDirectory);
        string temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}{extension}");
        try
        {
            await using (FileStream destination = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(destination, cancellationToken);
            }

            if (extension is ".docx" or ".xlsx" or ".pptx")
            {
                IReadOnlyList<OfficeOpenXmlTextUnit> units = officeExtractor.Extract(temporaryPath, extension);
                return units
                    .Where(unit => !string.IsNullOrWhiteSpace(unit.Text))
                    .Select(unit => new ArchiveEntryText(unit.Text.Trim(), $" | unita {unit.UnitNumber}"))
                    .ToArray();
            }

            using PdfDocument pdf = OpenPdf(temporaryPath);
            List<ArchiveEntryText> pages = [];
            for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = ExtractPdfPageText(pdf, pageNumber, forceOcr: false).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    pages.Add(new ArchiveEntryText(text, $" | pagina {pageNumber}"));
                }
            }

            return pages;
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static Task SaveArchiveProgressAsync(
        ImportedDocument document,
        int entryNumber,
        int pageCount,
        int chunkCount,
        string currentStep,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        CancellationToken cancellationToken)
    {
        return saveProgressAsync(
            new DocumentIngestionProgress(
                50,
                currentStep,
                new DocumentIngestionCheckpoint(
                    1,
                    document.Id,
                    entryNumber + 1,
                    pageCount,
                    chunkCount,
                    "archive")),
            cancellationToken);
    }

    private sealed record ArchiveEntryText(string Text, string PageSuffix);

    private sealed record ArchiveEntryProcessingResult(
        ArchiveManifestStatus Status,
        string? Error,
        int PageCount,
        int ChunkCount);

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
            await ExtractAndSaveGraphAsync(document.Id, chunks, cancellationToken);

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
            throw new InvalidOperationException($"{DescribeOfficeFormat(extension)} non leggibile o non valido.", ex);
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

        using PdfDocument pdf = OpenPdf(document.OriginalPath);
        int totalPages = pdf.NumberOfPages;
        int nextPage = Math.Max(1, checkpoint.NextBlock);
        int nextChunkOrdinal = Math.Max(0, checkpoint.NextChunkOrdinal);
        int chunkCount = nextChunkOrdinal;
        int processedPageCount = Math.Max(0, checkpoint.PageCount);

        for (int pageNumber = nextPage; pageNumber <= totalPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text = ExtractPdfPageText(pdf, pageNumber, forceOcr);
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
                    ? "Runtime OCR non installato. Apri Impostazioni > Diagnostica e usa Installa OCR."
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

    private async Task<OcrPipelineOptions> LoadOcrOptionsAsync(
        string? languageOverride,
        CancellationToken cancellationToken)
    {
        OcrProcessingSettings processingSettings = await ocrSettingsStore.GetProcessingAsync(cancellationToken);
        OcrSettings ocrSettings = await ocrSettingsStore.GetAsync(cancellationToken);

        return OcrPipelineOptions.Normalize(
            string.IsNullOrWhiteSpace(languageOverride) ? processingSettings.Language : languageOverride,
            processingSettings.MaxRetries,
            processingSettings.PageTimeoutSeconds,
            processingSettings.LowConfidenceThreshold,
            null,
            ocrSettings);
    }

    private static PdfDocument OpenPdf(string originalPath)
    {
        try
        {
            return PdfDocument.Open(originalPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Impossibile aprire il file PDF. Il file potrebbe essere cifrato, danneggiato o in un formato non supportato. Dettaglio: {ex.Message}", ex);
        }
    }

    private static string ExtractPdfPageText(PdfDocument pdf, int pageNumber, bool forceOcr)
    {
        if (forceOcr)
        {
            return string.Empty;
        }

        try
        {
            Page pdfPage = pdf.GetPage(pageNumber);
            string formatted = PdfLayoutTextExtractor.ExtractFormattedText(pdfPage);
            return string.IsNullOrWhiteSpace(formatted) ? pdfPage.Text.Trim() : formatted;
        }
        catch
        {
            return string.Empty;
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

    public static DocumentIngestionCheckpoint? ReadCheckpoint(string? checkpointJson)
    {
        if (string.IsNullOrWhiteSpace(checkpointJson))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<DocumentIngestionCheckpoint>(checkpointJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task ExtractAndSaveGraphAsync(long documentId, IReadOnlyList<IngestedDocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (graphExtractor == null || graphService == null || chunks.Count == 0) return;
        try
        {
            var allNodes = new List<OnlyRag.Core.EntityGraphNode>();
            var allEdges = new List<OnlyRag.Core.EntityGraphEdge>();
            long fakeChunkId = 1;
            foreach (var chunk in chunks)
            {
                var (nodes, edges) = graphExtractor.ExtractGraph(documentId, fakeChunkId++, chunk.Text);
                allNodes.AddRange(nodes);
                allEdges.AddRange(edges);
            }
            if (allNodes.Count > 0 || allEdges.Count > 0)
            {
                await graphService.InsertGraphAsync(allNodes, allEdges, cancellationToken);
            }
        }
        catch { }
    }
}

