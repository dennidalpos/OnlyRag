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
        Assert.Equal(1, current.MaxParallelJobs);
        Assert.Equal(1, current.MaxOcrParallelPages);
        Assert.Equal(1, current.EmbeddingBatchSize);
        Assert.Equal(1, current.TranslationBatchSize);
        Assert.Equal(8, current.MaxContextChunks);
        Assert.Equal(180, current.RequestTimeoutSeconds);
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
    public async Task OfficeConversionSettings_InvalidPathReturnsRequiresAdditionalComponentStatus()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OfficeConversionSettings request = new(
            @"C:\OnlyRag\Tests\MissingLibreOffice\soffice.exe",
            30);

        using HttpResponseMessage putResponse = await httpClient.PutAsJsonAsync("/api/settings/office-conversion", request);
        OfficeConversionSettings? saved = await putResponse.Content.ReadFromJsonAsync<OfficeConversionSettings>();
        OfficeConverterStatusResponse? status = await httpClient.GetFromJsonAsync<OfficeConverterStatusResponse>("/api/office-converter/status");

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
        Assert.NotNull(saved);
        Assert.Equal(request, saved);
        Assert.NotNull(status);
        Assert.False(status.IsAvailable);
        Assert.Equal("RequiresAdditionalComponent", status.State);
        Assert.Equal(30, status.ConversionTimeoutSeconds);
    }

    [Fact]
    public async Task DependencyOllamaStatus_ReturnsInstallCommandAndNetworkHint()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OllamaInstallStatus? status = await httpClient.GetFromJsonAsync<OllamaInstallStatus>("/api/dependencies/ollama");

        Assert.NotNull(status);
        Assert.Equal("https://ollama.com/download", status.InstallCommand);
        Assert.DoesNotContain("irm", status.InstallCommand, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iex", status.InstallCommand, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OLLAMA_HOST", status.NetworkAccessHint);
        Assert.Contains("policy aziendale", status.NetworkAccessHint);
    }

    [Fact]
    public async Task DependencyOcrStatus_DoesNotExposeDeveloperBootstrapInstruction()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        OcrProvisionStatus? status = await httpClient.GetFromJsonAsync<OcrProvisionStatus>("/api/dependencies/ocr");

        Assert.NotNull(status);
        Assert.DoesNotContain("Bootstrap", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bootstrap", status.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessLaunchingEndpoint_RejectsUnauthenticatedCall()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task ProcessLaunchingEndpoint_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task DependencyOcrProvision_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ocr/provision",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public void DependencyOcrProvision_SupportsOnlyPaddleCompatiblePythonVersions()
    {
        Assert.False(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 9, 0)));
        Assert.True(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 10, 0)));
        Assert.True(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 13, 5)));
        Assert.False(DependencyProvisioningService.IsSupportedOcrPythonVersion(new Version(3, 14, 0)));
    }

    [Fact]
    public async Task DependencyOllamaInstall_RequiresExplicitConfirmation()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(false),
            JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(processLauncher.StartedProcesses);
    }

    [Fact]
    public async Task DependencyOllamaInstall_OpensManualDownloadPageWithoutRemoteScriptExecution()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.Equal("https://ollama.com/download", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public async Task DependencyOllamaInstall_ProcessLaunchFailureReturnsManualOfflinePolicyGuidance()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new()
        {
            TryStartErrorMessage = "blocked by enterprise policy: internal details"
        };
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/ollama/install",
            new ProcessLaunchRequest(true),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("offline", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("policy aziendale", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal details", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenLogsFolder_UsesExpectedExplorerDispatch()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.EndsWith("explorer.exe", startInfo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([Path.GetFullPath(tempDescriptor.Descriptor.StoragePaths.LogsDirectory)], startInfo.ArgumentList.ToArray());
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public async Task OpenLogsFolder_DoesNotReturnProcessFailureDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        string sensitivePath = Path.Combine(tempDescriptor.Root, "logs");
        FakeProcessLauncher processLauncher = new()
        {
            TryStartErrorMessage = $"Access denied to {sensitivePath}"
        };
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/diagnostics/open-logs-folder",
            new ProcessLaunchRequest(true),
            JsonOptions);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(sensitivePath, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("riferimento", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibreOfficeDownload_UsesExpectedUrlDispatch()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        FakeProcessLauncher processLauncher = new();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/dependencies/libreoffice/open-download",
            new ProcessLaunchRequest(true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.Equal("https://www.libreoffice.org/download/download-libreoffice/", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Empty(startInfo.ArgumentList);
    }
}

