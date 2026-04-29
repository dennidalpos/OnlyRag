using System.Security.Cryptography;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Ocr;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed class OcrPipelineTests
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
        ImportedDocument firstDocument = await storage.CreateBinaryDocumentAsync("scan-a.jpg", Encoding.UTF8.GetBytes("fake-image"));
        ImportedDocument secondDocument = await storage.CreateBinaryDocumentAsync("scan-b.jpg", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("cached text", 0.94d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await service.IngestAsync(firstDocument, checkpoint: null, (_, _) => Task.CompletedTask);
        await service.IngestAsync(secondDocument, checkpoint: null, (_, _) => Task.CompletedTask);

        IReadOnlyList<string> pages = await storage.ReadPageTextsAsync(secondDocument.Id);
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
    public async Task IngestAsync_Image_UsesPersistedOcrSettingsAndInvalidatesCache()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore settingsStore = new(storage.Settings);
        ImportedDocument firstDocument = await storage.CreateBinaryDocumentAsync("settings-a.jpg", Encoding.UTF8.GetBytes("fake-image"));
        ImportedDocument secondDocument = await storage.CreateBinaryDocumentAsync("settings-b.jpg", Encoding.UTF8.GetBytes("fake-image"));
        FakeOcrEngine engine = new("settings text", 0.94d);
        DocumentIngestionService service = storage.CreateIngestionService(engine);

        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "balanced", PdfDpi = 200 });
        await service.IngestAsync(firstDocument, checkpoint: null, (_, _) => Task.CompletedTask);
        await settingsStore.UpdateAsync(OcrSettings.Default with { Profile = "accurate", PdfDpi = 300 });
        await service.IngestAsync(secondDocument, checkpoint: null, (_, _) => Task.CompletedTask);

        Assert.Equal(2, engine.RecognizeCount);
        Assert.Equal("accurate", engine.LastSettings?.Profile);
        Assert.Equal(300, engine.LastPreparationSettings?.PdfDpi);
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        private readonly string text;
        private readonly double confidence;

        public FakeOcrEngine(string text, double confidence)
        {
            this.text = text;
            this.confidence = confidence;
        }

        public int PrepareCount { get; private set; }

        public int RecognizeCount { get; private set; }

        public string? LastLanguage { get; private set; }

        public OcrSettings? LastSettings { get; private set; }

        public OcrSettings? LastPreparationSettings { get; private set; }

        public string EngineName => "FakeOCR";

        public string EngineVersion => "1.0";

        public string PreprocessVersion => "fake-preprocess-v1";

        public Task<OcrEngineAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new OcrEngineAvailability(true, EngineName, EngineVersion, null));
        }

        public async Task<OcrPagePreparation> PreparePageAsync(
            OcrPagePreparationRequest request,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            LastPreparationSettings = request.Settings;
            Directory.CreateDirectory(request.OutputDirectory);
            string output = Path.Combine(request.OutputDirectory, $"{Path.GetFileNameWithoutExtension(request.SourcePath)}-{request.PageNumber}.png");
            await File.WriteAllTextAsync(output, $"stable-prepared-page:{request.PageNumber}", cancellationToken);
            string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(output, cancellationToken))).ToLowerInvariant();
            return new OcrPagePreparation(output, hash, 100, 100);
        }

        public Task<OcrPageResult> RecognizeAsync(
            OcrRecognitionRequest request,
            CancellationToken cancellationToken = default)
        {
            RecognizeCount++;
            LastLanguage = request.Language;
            LastSettings = request.Settings;
            return Task.FromResult(new OcrPageResult(text, [], confidence, EngineName, EngineVersion, request.Language));
        }
    }

    private sealed class TempStorage : IDisposable
    {
        private TempStorage(string root)
        {
            Root = root;
            Paths = AppStoragePaths.FromRoot(root);
            Descriptor = new LocalSqliteStoreDescriptor(Paths);
            ConnectionFactory = new LocalSqliteConnectionFactory(Descriptor);
            Documents = new SqliteDocumentRepository(ConnectionFactory);
            Settings = new SqliteSettingsRepository(ConnectionFactory);
            OcrCache = new SqliteOcrCacheRepository(ConnectionFactory);
        }

        public string Root { get; }

        public AppStoragePaths Paths { get; }

        public LocalSqliteStoreDescriptor Descriptor { get; }

        public LocalSqliteConnectionFactory ConnectionFactory { get; }

        public SqliteDocumentRepository Documents { get; }

        public SqliteSettingsRepository Settings { get; }

        public SqliteOcrCacheRepository OcrCache { get; }

        public static async Task<TempStorage> CreateInitializedAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.Ocr.Tests", Guid.NewGuid().ToString("N"));
            TempStorage storage = new(root);
            LocalSqliteMigrator migrator = new(storage.Descriptor, storage.ConnectionFactory);
            LocalSqliteStorageService service = new(storage.Descriptor, migrator);
            await service.InitializeAsync();
            return storage;
        }

        public DocumentIngestionService CreateIngestionService(IOcrEngine engine)
        {
            return new DocumentIngestionService(
                Documents,
                Settings,
                new DocumentTextChunker(),
                new OfficeOpenXmlTextExtractor(),
                engine,
                OcrCache,
                new OcrRetryPolicy(),
                Descriptor);
        }

        public async Task<ImportedDocument> CreateBinaryDocumentAsync(string fileName, byte[] content)
        {
            Directory.CreateDirectory(Paths.DocumentOriginalsDirectory);
            string path = Path.Combine(Paths.DocumentOriginalsDirectory, fileName);
            await File.WriteAllBytesAsync(path, content);
            string extension = Path.GetExtension(fileName);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return await Documents.CreateAsync(new CreateDocumentRecordRequest(
                Guid.NewGuid().ToString("N"),
                fileName,
                path,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
                DocumentFileTypeDetector.DetectMimeType(fileName),
                extension,
                content.Length,
                DocumentStatus.Imported,
                PageCount: 0,
                CurrentJobId: null,
                LastError: null,
                now,
                now));
        }

        public async Task<IReadOnlyList<string>> ReadPageTextsAsync(long documentId)
        {
            await using Microsoft.Data.Sqlite.SqliteConnection connection = await ConnectionFactory.OpenConnectionAsync();
            await using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT text_content
                FROM document_pages
                WHERE document_id = $documentId
                ORDER BY page_number;
                """;
            command.Parameters.AddWithValue("$documentId", documentId);

            List<string> pages = [];
            await using Microsoft.Data.Sqlite.SqliteDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                pages.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
            }

            return pages;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private static byte[] CreateSinglePageTextPdf(string text)
    {
        string escapedText = text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal);
        string stream = $"BT /F1 24 Tf 100 700 Td ({escapedText}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream"
        ];

        StringBuilder builder = new();
        List<int> offsets = [];
        builder.Append("%PDF-1.4\n");
        for (int index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append('\n')
            .Append("0000000000 65535 f \n");
        foreach (int offset in offsets)
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\n")
            .Append("startxref\n").Append(xrefOffset).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
