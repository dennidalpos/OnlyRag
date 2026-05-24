using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task OllamaSettings_CanBeSavedAndReadBack()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OllamaSettings request = new(
            "http://192.168.1.40:11434",
            "chat-model",
            "embedding-model",
            "translation-model",
            180,
            2,
            TrustNonLocalEndpoint: true);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ollama", request);
        OllamaSettings? saved = await putResponse.Content.ReadFromJsonAsync<OllamaSettings>();
        OllamaSettings? current = await httpClient.GetFromJsonAsync<OllamaSettings>("/api/settings/ollama");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(request, saved);
        Assert.Equal(request, current);
    }

    [Fact]
    public async Task OllamaSettings_ChatAndTranslationNumCtxPersistAutomaticAndManualValues()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OllamaSettings automaticRequest = new(
            "http://localhost:11434",
            "chat-model",
            "embedding-model",
            "translation-model",
            180,
            2,
            EmbeddingNumCtx: 4096,
            ChatNumCtx: null,
            TranslationNumCtx: null);

        using HttpResponseMessage automaticResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            automaticRequest,
            JsonOptions);
        OllamaSettings? automaticSaved = await automaticResponse.Content.ReadFromJsonAsync<OllamaSettings>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, automaticResponse.StatusCode);
        Assert.NotNull(automaticSaved);
        Assert.Null(automaticSaved.ChatNumCtx);
        Assert.Null(automaticSaved.TranslationNumCtx);

        OllamaSettings manualRequest = automaticSaved with
        {
            ChatNumCtx = 8192,
            TranslationNumCtx = 16384
        };

        using HttpResponseMessage manualResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            manualRequest,
            JsonOptions);
        OllamaSettings? manualSaved = await manualResponse.Content.ReadFromJsonAsync<OllamaSettings>(JsonOptions);
        OllamaSettings? current = await httpClient.GetFromJsonAsync<OllamaSettings>("/api/settings/ollama", JsonOptions);

        Assert.Equal(HttpStatusCode.OK, manualResponse.StatusCode);
        Assert.NotNull(manualSaved);
        Assert.NotNull(current);
        Assert.Equal(8192, manualSaved.ChatNumCtx);
        Assert.Equal(16384, manualSaved.TranslationNumCtx);
        Assert.Equal(manualSaved, current);
    }

    [Fact]
    public async Task PerformanceSettings_CanBeSavedAndReadBackWithLowResourceLimits()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        PerformanceSettings request = new(
            MaxParallelJobs: 4,
            MaxOcrParallelPages: 4,
            EmbeddingBatchSize: 8,
            TranslationBatchSize: 4,
            MaxContextChunks: 16,
            RequestTimeoutSeconds: 180,
            EnableLowResourceMode: true);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/performance", request);
        PerformanceSettings? saved = await putResponse.Content.ReadFromJsonAsync<PerformanceSettings>();
        PerformanceSettings? current = await httpClient.GetFromJsonAsync<PerformanceSettings>("/api/settings/performance");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.True(current.EnableLowResourceMode);
        Assert.Equal(PerformanceProfileNames.Eco, current.Profile);
        Assert.Equal(PerformanceProfileNames.Eco, current.EffectiveProfile);
        Assert.Equal(1, current.MaxParallelJobs);
        Assert.Equal(1, current.MaxOcrParallelPages);
        Assert.Equal(1, current.EmbeddingBatchSize);
        Assert.Equal(1, current.TranslationBatchSize);
        Assert.Equal(6, current.MaxContextChunks);
        Assert.Equal(180, current.RequestTimeoutSeconds);
    }

    [Fact]
    public async Task PerformanceSettings_ProfilePresetNormalizesAndPersists()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        PerformanceSettings request = new(
            MaxParallelJobs: 1,
            MaxOcrParallelPages: 1,
            EmbeddingBatchSize: 1,
            TranslationBatchSize: 1,
            MaxContextChunks: 6,
            RequestTimeoutSeconds: 180,
            EnableLowResourceMode: false,
            Profile: PerformanceProfileNames.Power);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/performance",
            request,
            JsonOptions);
        PerformanceSettings? saved = await putResponse.Content.ReadFromJsonAsync<PerformanceSettings>(JsonOptions);
        PerformanceSettings? current = await httpClient.GetFromJsonAsync<PerformanceSettings>(
            "/api/settings/performance",
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(PerformanceProfileNames.Power, current.Profile);
        Assert.Equal(PerformanceProfileNames.Power, current.EffectiveProfile);
        Assert.False(current.EnableLowResourceMode);
        Assert.Equal(4, current.MaxParallelJobs);
        Assert.Equal(4, current.MaxOcrParallelPages);
        Assert.Equal(4, current.EmbeddingBatchSize);
        Assert.Equal(2, current.TranslationBatchSize);
        Assert.Equal(12, current.MaxContextChunks);
        Assert.Equal(120, current.RequestTimeoutSeconds);
    }

    [Fact]
    public async Task OllamaModelEndpoints_AcceptNamesWithSlashAndTag()
    {
        await using FakeOllamaServer ollama = await FakeOllamaServer.StartAsync();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        OllamaSettings settings = new(
            ollama.BaseUrl,
            "chat-model",
            "embed-model",
            "translation-model",
            180,
            2);
        string modelName = "namespace/model:tag";
        string escapedModelName = Uri.EscapeDataString(modelName);

        using HttpResponseMessage settingsResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            settings,
            JsonOptions);
        using HttpResponseMessage detailsResponse = await httpClient.GetAsync(
            $"/api/ollama/models/details?name={escapedModelName}");
        OllamaModelDetails? details = await detailsResponse.Content.ReadFromJsonAsync<OllamaModelDetails>(JsonOptions);
        using HttpResponseMessage deleteResponse = await httpClient.DeleteAsync(
            $"/api/ollama/models?name={escapedModelName}");

        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        Assert.NotNull(details);
        Assert.Equal(modelName, details.Name);
        Assert.Equal(4096, details.NumCtx);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.Contains(modelName, ollama.ShownModels);
        Assert.Contains(modelName, ollama.DeletedModels);
    }

    [Fact]
    public async Task OcrSettings_CanBeSavedAndReadBackNormalized()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrSettings request = new(
            Profile: "accurate",
            PdfDpi: 300,
            ModelPreset: "PP-OCRv5",
            ModelVersion: "PP-OCRv5",
            DetectionSideLimit: 1280,
            DetectionThreshold: 0.25d,
            DetectionBoxThreshold: 0.55d,
            DetectionUnclipRatio: 1.7d,
            RecognitionScoreThreshold: 0.45d,
            UseTextlineOrientation: false,
            UseDocumentOrientationClassification: true,
            UseDocumentUnwarping: true,
            RecognitionBatchSize: 12,
            CpuThreads: 4,
            Device: "cpu");

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ocr", request);
        OcrSettings? saved = await putResponse.Content.ReadFromJsonAsync<OcrSettings>();
        OcrSettings? current = await httpClient.GetFromJsonAsync<OcrSettings>("/api/settings/ocr");
        OcrSettings expected = OcrSettings.Normalize(request);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(expected, saved);
        Assert.Equal(expected, current);
    }

    [Fact]
    public async Task OcrSettings_RejectsGpuWhenCapabilityCheckFails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrSettings request = OcrSettings.Default with
        {
            Device = "gpu"
        };

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/ocr",
            request,
            JsonOptions);
        string body = await putResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
        Assert.Contains("ocr_gpu_unavailable", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OcrSettingsAutoEnableGpu_ReturnsCurrentSettingsWhenCapabilityCheckFails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsync(
            "/api/settings/ocr/auto-enable-gpu",
            null);
        OcrAutoGpuEnableResponse? body =
            await response.Content.ReadFromJsonAsync<OcrAutoGpuEnableResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.Applied);
        Assert.Equal("cpu", body.Settings.Device);
    }

    [Fact]
    public async Task IngestionSettings_CanBeSavedAndReadBackNormalized()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        IngestionSettings request = new(
            ChunkSizeTokens: 80,
            OverlapTokens: 900);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ingestion", request);
        IngestionSettings? saved = await putResponse.Content.ReadFromJsonAsync<IngestionSettings>();
        IngestionSettings? current = await httpClient.GetFromJsonAsync<IngestionSettings>("/api/settings/ingestion");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(new IngestionSettings(100, 50), saved);
        Assert.Equal(saved, current);
    }

    [Fact]
    public async Task OcrProcessingSettings_CanBeSavedAndReadBackNormalized()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrProcessingSettings request = new(
            Language: "de",
            MaxRetries: 7,
            PageTimeoutSeconds: 900,
            LowConfidenceThreshold: 0);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/ocr-processing", request);
        OcrProcessingSettings? saved = await putResponse.Content.ReadFromJsonAsync<OcrProcessingSettings>();
        OcrProcessingSettings? current = await httpClient.GetFromJsonAsync<OcrProcessingSettings>("/api/settings/ocr-processing");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(new OcrProcessingSettings("de", 2, 600, 0.01d), saved);
        Assert.Equal(saved, current);
    }

    [Fact]
    public async Task OfficeConversionSettings_NonLibreOfficeExecutableIsRejected()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        string executablePath = Path.Combine(tempDescriptor.Root, "tools", "not-libreoffice.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        await File.WriteAllTextAsync(executablePath, "not libreoffice");

        OfficeConversionSettings request = new(
            executablePath,
            30);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/office-conversion", request);
        string body = await putResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, putResponse.StatusCode);
        Assert.Contains("office_conversion_invalid_configuration", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfficeConversionSettings_ValidSofficePathCanBeSavedAndReadBack()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        string executablePath = Path.Combine(tempDescriptor.Root, "LibreOffice", "program", "soffice.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        await File.WriteAllTextAsync(executablePath, "fake soffice");

        OfficeConversionSettings request = new(executablePath, 30);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/office-conversion", request);
        OfficeConversionSettings? saved = await putResponse.Content.ReadFromJsonAsync<OfficeConversionSettings>();
        OfficeConversionSettings? current = await httpClient.GetFromJsonAsync<OfficeConversionSettings>("/api/settings/office-conversion");
        OfficeConverterStatusResponse? status = await httpClient.GetFromJsonAsync<OfficeConverterStatusResponse>("/api/office-converter/status");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.NotNull(current);
        Assert.Equal(Path.GetFullPath(executablePath), saved.LibreOfficePath);
        Assert.Equal(saved, current);
        Assert.NotNull(status);
        Assert.True(status.IsAvailable);
        Assert.Equal("Available", status.State);
        Assert.Equal(30, status.ConversionTimeoutSeconds);
    }
}
