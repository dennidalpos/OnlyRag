using OnlyRag.Core;
using OnlyRag.Infrastructure.Ocr;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class OcrPipelineTests
{
    [Fact]
    public void OcrSettings_NormalizeUnknownProfileUsesBalancedPreset()
    {
        OcrSettings normalized = OcrSettings.Normalize(new OcrSettings(
            Profile: "unknown",
            PdfDpi: 999,
            DetectionSideLimit: 100,
            DetectionThreshold: 2,
            RecognitionScoreThreshold: 0,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 100,
            CpuThreads: 100,
            Device: "bad"));

        Assert.Equal("balanced", normalized.Profile);
        Assert.Equal(220, normalized.PdfDpi);
        Assert.Equal(1152, normalized.DetectionSideLimit);
        Assert.Equal(0.30d, normalized.DetectionThreshold);
        Assert.Equal(0.60d, normalized.DetectionBoxThreshold);
        Assert.Equal(1.50d, normalized.DetectionUnclipRatio);
        Assert.Equal(0.50d, normalized.RecognitionScoreThreshold);
        Assert.Equal(6, normalized.RecognitionBatchSize);
        Assert.Equal(2, normalized.CpuThreads);
        Assert.Equal("auto", normalized.Device);
    }

    [Fact]
    public void OcrSettings_NormalizeCustomClampsUnsafeValues()
    {
        OcrSettings normalized = OcrSettings.Normalize(new OcrSettings(
            Profile: "custom",
            PdfDpi: 999,
            DetectionSideLimit: 100,
            DetectionThreshold: 2,
            RecognitionScoreThreshold: 0,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 100,
            CpuThreads: 100,
            Device: "bad"));

        Assert.Equal("custom", normalized.Profile);
        Assert.Equal(400, normalized.PdfDpi);
        Assert.Equal(320, normalized.DetectionSideLimit);
        Assert.Equal(0.99d, normalized.DetectionThreshold);
        Assert.Equal(0.60d, normalized.DetectionBoxThreshold);
        Assert.Equal(1.50d, normalized.DetectionUnclipRatio);
        Assert.Equal(0.01d, normalized.RecognitionScoreThreshold);
        Assert.Equal(32, normalized.RecognitionBatchSize);
        Assert.Equal(16, normalized.CpuThreads);
        Assert.Equal("auto", normalized.Device);
    }

    [Theory]
    [InlineData("fast", 160, 896, 0.38d, 0.58d, false, false, 4, 1)]
    [InlineData("balanced", 220, 1152, 0.30d, 0.50d, false, false, 6, 2)]
    [InlineData("accurate", 300, 1536, 0.23d, 0.42d, true, true, 8, 4)]
    public void OcrSettings_NormalizeAppliesProfilePresets(
        string profile,
        int pdfDpi,
        int detectionSideLimit,
        double detectionThreshold,
        double recognitionScoreThreshold,
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
        Assert.Equal(0.60d, normalized.DetectionBoxThreshold);
        Assert.Equal(1.50d, normalized.DetectionUnclipRatio);
        Assert.Equal(recognitionScoreThreshold, normalized.RecognitionScoreThreshold);
        Assert.True(normalized.UseTextlineOrientation);
        Assert.Equal(useDocumentOrientationClassification, normalized.UseDocumentOrientationClassification);
        Assert.Equal(useDocumentUnwarping, normalized.UseDocumentUnwarping);
        Assert.Equal(recognitionBatchSize, normalized.RecognitionBatchSize);
        Assert.Equal(cpuThreads, normalized.CpuThreads);
        Assert.Equal("PP-OCRv5", normalized.ModelPreset);
        Assert.Equal("PP-OCRv5", normalized.ModelVersion);
        Assert.Equal("auto", normalized.Device);
    }

    [Fact]
    public void OcrSettings_NormalizeProfilePreservesConfiguredDevice()
    {
        OcrSettings normalized = OcrSettings.Normalize(OcrSettings.Default with
        {
            Profile = "accurate",
            Device = "gpu"
        });

        Assert.Equal("accurate", normalized.Profile);
        Assert.Equal("gpu", normalized.Device);
        Assert.Equal(16, normalized.RecognitionBatchSize);
    }

    [Theory]
    [InlineData("fast", 8)]
    [InlineData("balanced", 12)]
    [InlineData("accurate", 16)]
    public void OcrSettings_GpuProfilePresetsUseLargerRecognitionBatches(
        string profile,
        int expectedBatchSize)
    {
        OcrSettings normalized = OcrSettings.Normalize(OcrSettings.Default with
        {
            Profile = profile,
            Device = "gpu"
        });

        Assert.Equal(profile, normalized.Profile);
        Assert.Equal("gpu", normalized.Device);
        Assert.Equal(expectedBatchSize, normalized.RecognitionBatchSize);
    }

    [Fact]
    public async Task OcrSettingsStore_PersistsNormalizedSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        OcrSettings saved = await store.UpdateAsync(new OcrSettings(
            Profile: "accurate",
            PdfDpi: 300,
            DetectionSideLimit: 1280,
            DetectionThreshold: 0.25d,
            RecognitionScoreThreshold: 0.45d,
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
            DetectionSideLimit: 1152,
            DetectionThreshold: 0.28d,
            RecognitionScoreThreshold: 0.48d,
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
    public async Task OcrSettingsStore_AutoEnableGpuAppliesOnlyWithoutManualDevicePreference()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        OcrAutoGpuEnableResponse response = await store.AutoEnableGpuAsync();
        OcrSettings loaded = await store.GetAsync();

        Assert.True(response.Applied);
        Assert.Equal("gpu", response.Settings.Device);
        Assert.Equal("gpu", loaded.Device);
        Assert.Equal(12, loaded.RecognitionBatchSize);
    }

    [Fact]
    public async Task OcrSettingsStore_AutoEnableGpuPreservesManualCpuPreference()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        await store.UpdateAsync(OcrSettings.Default with { Device = "cpu" });
        OcrAutoGpuEnableResponse response = await store.AutoEnableGpuAsync();
        OcrSettings loaded = await store.GetAsync();

        Assert.False(response.Applied);
        Assert.Equal("cpu", response.Settings.Device);
        Assert.Equal("cpu", loaded.Device);
    }

    [Fact]
    public async Task OcrSettingsStore_PersistsNormalizedProcessingSettings()
    {
        using TempStorage storage = await TempStorage.CreateInitializedAsync();
        OcrSettingsStore store = new(storage.Settings);

        OcrProcessingSettings saved = await store.UpdateProcessingAsync(new OcrProcessingSettings(
            Language: "fr",
            MaxRetries: 9,
            PageTimeoutSeconds: 1,
            LowConfidenceThreshold: 2));
        OcrProcessingSettings loaded = await store.GetProcessingAsync();

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
            RecognitionScoreThreshold = 0.45d,
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
    public void PaddleOcrEngine_BuildRecognizeArgumentsIncludesGpuDevice()
    {
        OcrSettings settings = OcrSettings.Default with
        {
            Profile = "custom",
            Device = "gpu"
        };

        string[] arguments = PaddleOcrEngine.BuildRecognizeArguments(
            new OcrRecognitionRequest("prepared.png", "it", settings));

        Assert.Contains("--device", arguments);
        Assert.Contains("gpu", arguments);
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
    public void OcrCacheKey_ChangesWhenDeviceChanges()
    {
        string cpu = OcrCacheKey.Create(
            "page-hash",
            "PaddleOCR",
            "1.0",
            "it",
            "prep-v1",
            (OcrSettings.Default with { Device = "cpu" }).ToCacheSignature());
        string gpu = OcrCacheKey.Create(
            "page-hash",
            "PaddleOCR",
            "1.0",
            "it",
            "prep-v1",
            (OcrSettings.Default with { Device = "gpu" }).ToCacheSignature());

        Assert.NotEqual(cpu, gpu);
    }

    [Fact]
    public void OcrCacheSignature_IgnoresProfileLabelWhenEffectiveSettingsMatch()
    {
        OcrSettings accurate = OcrSettings.Normalize(OcrSettings.Default with { Profile = "accurate" });
        OcrSettings customWithAccurateValues = accurate with { Profile = "custom" };

        Assert.Equal(accurate.ToCacheSignature(), customWithAccurateValues.ToCacheSignature());
    }
}
