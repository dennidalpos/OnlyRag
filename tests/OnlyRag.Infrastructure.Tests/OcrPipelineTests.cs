using System.Security.Cryptography;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class OcrPipelineTests
{
    [Fact]
    public async Task OcrCacheRepository_UpsertsAndReadsByCompositeCacheKey()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        SqliteOcrCacheRepository cache = new(storage.ConnectionFactory);
        string cacheKey = OcrCacheKey.Create("page-hash", "PaddleOCR", "1.0", "it", "prep-v1");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await cache.UpsertAsync(new OcrCacheEntry(
            cacheKey,
            "page-hash",
            "PaddleOCR",
            "1.0",
            "it",
            "prep-v1",
            "testo",
            """[{"text":"testo"}]""",
            0.91d,
            now,
            now));

        OcrCacheEntry? cached = await cache.GetAsync(cacheKey);

        Assert.NotNull(cached);
        Assert.Equal("testo", cached.Text);
        Assert.Equal(0.91d, cached.Confidence);
    }

    [Fact]
    public async Task RetryPolicy_RetriesFailuresAndLowConfidenceAtMostTwice()
    {
        OcrRetryPolicy policy = new();
        int attempts = 0;

        OcrPageResult result = await policy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                {
                    return Task.FromResult(new OcrPageResult(
                        "low",
                        [],
                        0.2d,
                        "FakeOCR",
                        "1.0",
                        "it"));
                }

                return Task.FromResult(new OcrPageResult(
                    "ok",
                    [],
                    0.95d,
                    "FakeOCR",
                    "1.0",
                    "it"));
            },
            OcrPipelineOptions.Normalize("it", maxRetries: 2, pageTimeoutSeconds: 15, lowConfidenceThreshold: 0.8d));

        Assert.Equal(3, attempts);
        Assert.Equal("ok", result.Text);
    }

    [Fact]
    public void OcrPipelineOptions_NormalizeReadsMaxParallelPages()
    {
        OcrPipelineOptions options = OcrPipelineOptions.Normalize(
            "it",
            maxRetries: 2,
            pageTimeoutSeconds: 15,
            lowConfidenceThreshold: 0.8d,
            maxParallelPages: 3);

        Assert.Equal(3, options.MaxParallelPages);
    }

    [Fact]
    public async Task RetryPolicy_IncludesLastFailureInConfiguredRetryError()
    {
        OcrRetryPolicy policy = new();

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ExecuteAsync(
                _ => throw new InvalidOperationException("Bridge PaddleOCR terminato con errore."),
                OcrPipelineOptions.Normalize("it", maxRetries: 0, pageTimeoutSeconds: 15, lowConfidenceThreshold: 0.8d)));

        Assert.Contains("Bridge PaddleOCR terminato con errore.", error.Message);
    }

    [Fact]
    public async Task IngestAsync_Image_UsesIOcrEngineAndSavesCheckpoint()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("scan.png", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("scan text", 0.93d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);
        List<DocumentIngestionProgress> progress = [];

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint: null,
            (item, _) =>
            {
                progress.Add(item);
                return Task.CompletedTask;
            });

        IReadOnlyList<string> pages = await storage.ReadPageTextsAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(1, result.ChunkCount);
        Assert.Single(pages);
        Assert.Equal("scan text", pages[0]);
        Assert.True(progress.Count >= 2);
        Assert.Equal(2, progress[^1].Checkpoint.NextBlock);
        Assert.Equal(1, engine.RecognizeCount);
    }

    [Fact]
    public async Task IngestAsync_Image_UsesOcrCacheOnSecondRun()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("scan.jpg", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("cached text", 0.94d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);
        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);

        IReadOnlyList<string> pages = await storage.ReadPageTextsAsync(document.Id);
        Assert.Equal("cached text", pages[0]);
        Assert.Equal(1, engine.RecognizeCount);
        Assert.Equal(2, engine.PrepareCount);
    }

    [Fact]
    public async Task IngestAsync_ScannedPdf_FallsBackToFakeOcrPageByPage()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("scan.pdf", CreateSinglePageTextPdf(string.Empty));
        FakeOcrEngine engine = new("pdf scan text", 0.9d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        DocumentIngestionResult result = await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);

        IReadOnlyList<string> pages = await storage.ReadPageTextsAsync(document.Id);
        Assert.Equal(1, result.PageCount);
        Assert.Equal("pdf scan text", pages[0]);
        Assert.Equal(1, engine.RecognizeCount);
    }

    [Fact]
    public async Task IngestAsync_ScannedPdfResumePreservesExistingChunkOrdinals()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        ImportedDocument document = await storage.CreateBinaryDocumentAsync(
            "resume-scan.pdf",
            CreateMultiPageTextPdf("First page text before OCR.", string.Empty));
        FakeOcrEngine engine = new("Second page OCR text.", 0.91d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);
        DocumentIngestionCheckpoint? checkpoint = null;

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await service.IngestAsync(
                document,
                checkpoint: null,
                (item, _) =>
                {
                    if (item.Checkpoint is { Mode: "ocr", NextBlock: 2 })
                    {
                        checkpoint = item.Checkpoint;
                        throw new OperationCanceledException();
                    }

                    return Task.CompletedTask;
                });
        });

        Assert.NotNull(checkpoint);
        Assert.Equal(1, checkpoint.NextChunkOrdinal);
        IReadOnlyList<string> chunksBeforeResume = await storage.ReadChunkTextsAsync(document.Id);
        Assert.Single(chunksBeforeResume);
        Assert.Contains("First page text before OCR.", chunksBeforeResume[0], StringComparison.Ordinal);

        DocumentIngestionResult result = await service.IngestAsync(
            document,
            checkpoint,
            (_, _) => Task.CompletedTask);

        IReadOnlyList<string> chunksAfterResume = await storage.ReadChunkTextsAsync(document.Id);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, result.ChunkCount);
        Assert.Equal(2, chunksAfterResume.Count);
        Assert.Contains("First page text before OCR.", chunksAfterResume[0], StringComparison.Ordinal);
        Assert.Contains("Second page OCR text.", chunksAfterResume[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task IngestAsync_Image_UsesJobOcrLanguageBeforeStoredDefault()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        await storage.Settings.UpsertAsync("ocr.language", "en");
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("scan-language.png", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("texte", 0.93d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask,
            forceOcr: false,
            ocrLanguage: "fr");

        Assert.Equal("fr", engine.LastLanguage);
    }

    [Fact]
    public async Task IngestAsync_Image_UsesStoredOcrProcessingSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore settingsStore = new(storage.Settings);
        await settingsStore.UpdateProcessingAsync(new OcrProcessingSettings(
            Language: "en",
            MaxRetries: 0,
            PageTimeoutSeconds: 15,
            LowConfidenceThreshold: 0.99d));
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("scan-runtime.png", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("runtime text", 0.20d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await service.IngestAsync(
            document,
            checkpoint: null,
            (_, _) => Task.CompletedTask);

        Assert.Equal("en", engine.LastLanguage);
        Assert.Equal(1, engine.RecognizeCount);
    }

    [Fact]
    public async Task IngestAsync_Image_UsesPersistedOcrSettingsAndInvalidatesCache()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore settingsStore = new(storage.Settings);
        ImportedDocument document = await storage.CreateBinaryDocumentAsync("settings.jpg", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("settings text", 0.94d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "balanced", PdfDpi = 220 });
        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);
        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "accurate", PdfDpi = 300 });
        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);

        Assert.Equal(2, engine.RecognizeCount);
        Assert.Equal("accurate", engine.LastSettings?.Profile);
        Assert.Equal(300, engine.LastPreparationSettings?.PdfDpi);
    }
}
