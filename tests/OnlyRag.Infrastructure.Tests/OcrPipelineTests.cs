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
    public void OcrSettings_NormalizeUnknownProfileUsesBalancedPreset()
    {
        OcrSettings normalized = OcrSettings.Normalize(new OcrSettings(
            Profile: "unknown",
            PdfDpi: 999,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 100,
            DetectionThreshold: 2,
            DetectionBoxThreshold: -1,
            DetectionUnclipRatio: 9,
            RecognitionScoreThreshold: 0,
            UseTextlineOrientation: true,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 100,
            CpuThreads: 100,
            Device: "bad"));

        Assert.Equal("balanced", normalized.Profile);
        Assert.Equal(200, normalized.PdfDpi);
        Assert.Equal(960, normalized.DetectionSideLimit);
        Assert.Equal(0.30d, normalized.DetectionThreshold);
        Assert.Equal(0.60d, normalized.DetectionBoxThreshold);
        Assert.Equal(1.50d, normalized.DetectionUnclipRatio);
        Assert.Equal(0.50d, normalized.RecognitionScoreThreshold);
        Assert.Equal(6, normalized.RecognitionBatchSize);
        Assert.Equal(2, normalized.CpuThreads);
        Assert.Equal("cpu", normalized.Device);
    }

    [Fact]
    public void OcrSettings_NormalizeCustomClampsUnsafeValues()
    {
        OcrSettings normalized = OcrSettings.Normalize(new OcrSettings(
            Profile: "custom",
            PdfDpi: 999,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 100,
            DetectionThreshold: 2,
            DetectionBoxThreshold: -1,
            DetectionUnclipRatio: 9,
            RecognitionScoreThreshold: 0,
            UseTextlineOrientation: true,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 100,
            CpuThreads: 100,
            Device: "bad"));

        Assert.Equal("custom", normalized.Profile);
        Assert.Equal(400, normalized.PdfDpi);
        Assert.Equal(320, normalized.DetectionSideLimit);
        Assert.Equal(0.99d, normalized.DetectionThreshold);
        Assert.Equal(0.01d, normalized.DetectionBoxThreshold);
        Assert.Equal(3.0d, normalized.DetectionUnclipRatio);
        Assert.Equal(0.01d, normalized.RecognitionScoreThreshold);
        Assert.Equal(32, normalized.RecognitionBatchSize);
        Assert.Equal(16, normalized.CpuThreads);
        Assert.Equal("cpu", normalized.Device);
    }

    [Theory]
    [InlineData("fast", 150, 736, 0.35d, 0.65d, 1.40d, 0.55d, true, false, false, 4, 1)]
    [InlineData("balanced", 200, 960, 0.30d, 0.60d, 1.50d, 0.50d, true, false, false, 6, 2)]
    [InlineData("accurate", 300, 1280, 0.25d, 0.55d, 1.70d, 0.45d, true, true, true, 8, 4)]
    public void OcrSettings_NormalizeAppliesProfilePresets(
        string profile,
        int pdfDpi,
        int detectionSideLimit,
        double detectionThreshold,
        double detectionBoxThreshold,
        double detectionUnclipRatio,
        double recognitionScoreThreshold,
        bool useTextlineOrientation,
        bool useDocumentOrientationClassification,
        bool useDocumentUnwarping,
        int recognitionBatchSize,
        int cpuThreads)
    {
        OcrSettings normalized = OcrSettings.Normalize(OcrSettings.Default with
        {
            Profile = profile,
            PdfDpi = 400,
            DetectionSideLimit = 4096
        });

        Assert.Equal(profile, normalized.Profile);
        Assert.Equal(pdfDpi, normalized.PdfDpi);
        Assert.Equal(detectionSideLimit, normalized.DetectionSideLimit);
        Assert.Equal(detectionThreshold, normalized.DetectionThreshold);
        Assert.Equal(detectionBoxThreshold, normalized.DetectionBoxThreshold);
        Assert.Equal(detectionUnclipRatio, normalized.DetectionUnclipRatio);
        Assert.Equal(recognitionScoreThreshold, normalized.RecognitionScoreThreshold);
        Assert.Equal(useTextlineOrientation, normalized.UseTextlineOrientation);
        Assert.Equal(useDocumentOrientationClassification, normalized.UseDocumentOrientationClassification);
        Assert.Equal(useDocumentUnwarping, normalized.UseDocumentUnwarping);
        Assert.Equal(recognitionBatchSize, normalized.RecognitionBatchSize);
        Assert.Equal(cpuThreads, normalized.CpuThreads);
        Assert.Equal("PP-OCRv5", normalized.ModelPreset);
        Assert.Equal("PP-OCRv5", normalized.ModelVersion);
        Assert.Equal("cpu", normalized.Device);
    }

    [Fact]
    public async Task OcrSettingsStore_PersistsNormalizedSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        OcrSettings saved = await store.UpdateAsync(new OcrSettings(
            Profile: "accurate",
            PdfDpi: 300,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 1280,
            DetectionThreshold: 0.25d,
            DetectionBoxThreshold: 0.55d,
            DetectionUnclipRatio: 1.7d,
            RecognitionScoreThreshold: 0.45d,
            UseTextlineOrientation: true,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 8,
            CpuThreads: 4,
            Device: "cpu"));
        OcrSettings loaded = await store.GetAsync();

        Assert.Equal(saved, loaded);
        Assert.Equal("accurate", loaded.Profile);
        Assert.Equal(300, loaded.PdfDpi);
        Assert.True(loaded.UseTextlineOrientation);
        Assert.Equal(8, loaded.RecognitionBatchSize);
    }

    [Fact]
    public async Task OcrSettingsStore_PersistsCustomSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        OcrSettings request = new(
            Profile: "custom",
            PdfDpi: 260,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 1152,
            DetectionThreshold: 0.28d,
            DetectionBoxThreshold: 0.58d,
            DetectionUnclipRatio: 1.6d,
            RecognitionScoreThreshold: 0.48d,
            UseTextlineOrientation: false,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: false,
            RecognitionBatchSize: 10,
            CpuThreads: 3,
            Device: "cpu");

        OcrSettings saved = await store.UpdateAsync(request);
        OcrSettings loaded = await store.GetAsync();

        Assert.Equal(request, saved);
        Assert.Equal(request, loaded);
    }

    [Fact]
    public async Task OcrProcessingSettingsStore_PersistsNormalizedSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrProcessingSettingsStore store = new(storage.Settings);

        OcrProcessingSettings saved = await store.UpdateAsync(new OcrProcessingSettings(
            Language: "fr",
            MaxRetries: 9,
            PageTimeoutSeconds: 1,
            LowConfidenceThreshold: 2));
        OcrProcessingSettings loaded = await store.GetAsync();

        Assert.Equal("fr", saved.Language);
        Assert.Equal(2, saved.MaxRetries);
        Assert.Equal(15, saved.PageTimeoutSeconds);
        Assert.Equal(0.99d, saved.LowConfidenceThreshold);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void PaddleOcrEngine_BuildRecognizeArgumentsIncludesConfiguredControls()
    {
        OcrSettings settings = OcrSettings.Default with
        {
            Profile = "custom",
            PdfDpi = 300,
            DetectionSideLimit = 1280,
            DetectionThreshold = 0.25d,
            DetectionBoxThreshold = 0.55d,
            DetectionUnclipRatio = 1.7d,
            RecognitionScoreThreshold = 0.45d,
            UseTextlineOrientation = false,
            UseDocumentOrientationClassification = true,
            UseDocumentUnwarping = true,
            RecognitionBatchSize = 12,
            CpuThreads = 4,
            Device = "cpu"
        };

        string[] arguments = PaddleOcrEngine.BuildRecognizeArguments(
            new OcrRecognitionRequest("prepared.png", "it", settings));

        Assert.DoesNotContain("--profile", arguments);
        Assert.Contains("--detection-side-limit", arguments);
        Assert.Contains("1280", arguments);
        Assert.Contains("--recognition-score-threshold", arguments);
        Assert.Contains("0.45", arguments);
        Assert.Contains("--use-document-unwarping", arguments);
        Assert.Contains("true", arguments);
        Assert.Contains("--cpu-threads", arguments);
        Assert.Contains("4", arguments);
    }

    [Fact]
    public void OcrCacheKey_ChangesWhenRecognitionSettingsChange()
    {
        string balanced = OcrCacheKey.Create(
            "page-hash",
            "PaddleOCR",
            "1.0",
            "it",
            "prep-v1",
            OcrSettings.Default.ToCacheSignature());
        string accurate = OcrCacheKey.Create(
            "page-hash",
            "PaddleOCR",
            "1.0",
            "it",
            "prep-v1",
            (OcrSettings.Default with { Profile = "accurate" }).ToCacheSignature());

        Assert.NotEqual(balanced, accurate);
    }

    [Fact]
    public void OcrCacheSignature_IgnoresProfileLabelWhenEffectiveSettingsMatch()
    {
        OcrSettings accurate = OcrSettings.Normalize(OcrSettings.Default with { Profile = "accurate" });
        OcrSettings customWithAccurateValues = accurate with { Profile = "custom" };

        Assert.Equal(accurate.ToCacheSignature(), customWithAccurateValues.ToCacheSignature());
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
        OcrProcessingSettingsStore settingsStore = new(storage.Settings);
        await settingsStore.UpdateAsync(new OcrProcessingSettings(
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

        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "balanced", PdfDpi = 200 });
        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);
        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "accurate", PdfDpi = 300 });
        await service.IngestAsync(document, checkpoint: null, (_, _) => Task.CompletedTask);

        Assert.Equal(2, engine.RecognizeCount);
        Assert.Equal("accurate", engine.LastSettings?.Profile);
        Assert.Equal(300, engine.LastPreparationSettings?.PdfDpi);
    }
}
