using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using OnlyRag.Api.Images;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task ImageModelCatalog_ReturnsCuratedModelState()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-catalog-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        ImageModelCatalogEntry[]? catalog = await httpClient.GetFromJsonAsync<ImageModelCatalogEntry[]>("/api/images/models/catalog", JsonOptions);
        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);

        Assert.NotNull(catalog);
        Assert.True(catalog.Length >= 4);
        ImageModelCatalogEntry model = Assert.Single(catalog, candidate => candidate.Id == ImageModelCatalog.DefaultModelId);
        Assert.Equal(ImageModelCatalog.DefaultModelId, model.Id);
        Assert.True(model.IsBuiltIn);
        Assert.Contains(catalog, candidate => candidate.Id == "onlyrag-sdxl-turbo-directml");
        Assert.Contains(catalog, candidate => candidate.Id == "lcm-sdxl-olive-onnx");
        Assert.Contains(catalog, candidate => candidate.Id == "ffusion-sdxl-base-directml");
        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == model.Id);
        Assert.Equal(model.Id, state.ModelId);
        Assert.False(state.IsVerified);
        Assert.Equal("NotDownloaded", state.State);
    }

    [Fact]
    public async Task ImageGeneration_MissingModelReturnsClearError()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-missing-model-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.Conflict,
            "Modello immagini non pronto",
            "image_generation_model_not_ready");
    }

    [Fact]
    public void ImageGeneration_NormalizeUsesQualityFirstStepRange()
    {
        ImageGenerationRequest lowSteps = ImageGenerationRequestValidator.Normalize(
            new ImageGenerationRequest("portrait", null, null, 1024, 1024, 1, 1, null));
        ImageGenerationRequest highSteps = ImageGenerationRequestValidator.Normalize(
            new ImageGenerationRequest("portrait", null, null, 1024, 1024, 150, 1, null));

        Assert.Equal(4, lowSteps.Steps);
        Assert.Equal(40, highSteps.Steps);
    }

    [Fact]
    public void ImageGeneration_EngineAddsQualityPromptsForAnatomy()
    {
        string prompt = OnnxStableDiffusionImageGenerationEngine.CreateQualityPrompt("portrait of a person");
        string negativePrompt = OnnxStableDiffusionImageGenerationEngine.CreateNegativePrompt(null);
        string combinedNegativePrompt = OnnxStableDiffusionImageGenerationEngine.CreateNegativePrompt("washed out");

        Assert.Contains("coherent anatomy", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("realistic face", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bad anatomy", negativePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("extra fingers", negativePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("washed out,", combinedNegativePrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fused limbs", combinedNegativePrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageGeneration_EngineSelectsModelTypeFromCatalogModelId()
    {
        Assert.Equal(
            OnnxStack.StableDiffusion.Enums.ModelType.Base,
            OnnxStableDiffusionImageGenerationEngine.ResolveModelType(ImageModelCatalog.DefaultModelId));
        Assert.Equal(
            OnnxStack.StableDiffusion.Enums.ModelType.Turbo,
            OnnxStableDiffusionImageGenerationEngine.ResolveModelType("onlyrag-sdxl-turbo-directml"));
        Assert.Equal(
            OnnxStack.StableDiffusion.Enums.ModelType.Turbo,
            OnnxStableDiffusionImageGenerationEngine.ResolveModelType("lcm-sdxl-olive-onnx"));
    }

    [Fact]
    public async Task ImageGeneration_PlaceholderModelReturnsClearError()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-integrated-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeedPlaceholderImageModel(tempDescriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 46);
        await SaveImageSettingsAsync(httpClient);

        ImageGenerationRuntimeStatus? runtimeStatus =
            await httpClient.GetFromJsonAsync<ImageGenerationRuntimeStatus>("/api/images/runtime/status", JsonOptions);
        Assert.NotNull(runtimeStatus);
        Assert.False(runtimeStatus.IsReady);
        Assert.Equal("VerificationFailed", runtimeStatus.State);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.Conflict,
            "Modello immagini non pronto",
            "image_generation_model_not_ready");
    }

    [Fact]
    public async Task ImageModelDownload_RejectsMissingConsent()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-download-consent-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/images/models/{ImageModelCatalog.DefaultModelId}/download",
            new ImageModelDownloadRequest(ConsentConfirmed: false),
            JsonOptions);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Richiesta immagini non valida",
            "image_generation_invalid_request");
    }

    [Fact]
    public async Task ImageModelDownload_SkipsExistingValidModelFile()
    {
        const string modelId = "manual-incremental-valid";
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-download-incremental-valid-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        string sourcePath = Path.Combine(tempDescriptor.Root, "source-model.onnx");
        byte[] content = System.Text.Encoding.UTF8.GetBytes("valid-model-content");
        await File.WriteAllBytesAsync(sourcePath, content);
        string sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await UpsertManualImageModelCatalogAsync(httpClient, modelId, new Uri(sourcePath).AbsoluteUri, content.LongLength, sha256);
        string destinationPath = GetModelFilePath(tempDescriptor, modelId);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, content);
        DateTime oldTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(destinationPath, oldTimestamp);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/images/models/{modelId}/download",
            new ImageModelDownloadRequest(ConsentConfirmed: true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(oldTimestamp, File.GetLastWriteTimeUtc(destinationPath));
        Assert.Equal(content, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task ImageModelDownload_ReplacesCorruptModelFile()
    {
        const string modelId = "manual-incremental-corrupt";
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-download-incremental-corrupt-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        string sourcePath = Path.Combine(tempDescriptor.Root, "source-model.onnx");
        byte[] validContent = System.Text.Encoding.UTF8.GetBytes("valid-model-content");
        byte[] corruptContent = System.Text.Encoding.UTF8.GetBytes("invalid-model-data!");
        Assert.Equal(validContent.Length, corruptContent.Length);
        await File.WriteAllBytesAsync(sourcePath, validContent);
        string sha256 = Convert.ToHexString(SHA256.HashData(validContent)).ToLowerInvariant();
        await UpsertManualImageModelCatalogAsync(httpClient, modelId, new Uri(sourcePath).AbsoluteUri, validContent.LongLength, sha256);
        string destinationPath = GetModelFilePath(tempDescriptor, modelId);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await File.WriteAllBytesAsync(destinationPath, corruptContent);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            $"/api/images/models/{modelId}/download",
            new ImageModelDownloadRequest(ConsentConfirmed: true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(validContent, await File.ReadAllBytesAsync(destinationPath));
    }

    [Fact]
    public async Task ImageModelCatalog_AllowsManualModelWithEditableDownloadUrl()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-catalog-edit-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            "/api/images/models/catalog/manual-model",
            new ImageModelCatalogEntryRequest(
                "ignored-client-id",
                "Manual model",
                "Manual profile",
                "https://example.test/model.onnx",
                "Manual license",
                123,
                [ImageModelCatalog.RequiredModelFileName],
                string.Empty),
            JsonOptions);
        ImageModelCatalogEntry? payload = await response.Content.ReadFromJsonAsync<ImageModelCatalogEntry>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("manual-model", payload.Id);
        Assert.Equal("https://example.test/model.onnx", payload.DownloadUrl);
        Assert.False(payload.IsBuiltIn);

        ImageModelCatalogEntry[]? catalog = await httpClient.GetFromJsonAsync<ImageModelCatalogEntry[]>("/api/images/models/catalog", JsonOptions);
        Assert.NotNull(catalog);
        Assert.Contains(catalog, model => model.Id == "manual-model" && model.DownloadUrl == "https://example.test/model.onnx");
    }

    [Fact]
    public async Task ImageModelState_RejectsExistingPlaceholderModel()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-placeholder-state-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeedPlaceholderImageModel(tempDescriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 46);

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);
        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal("VerificationFailed", state.State);
        Assert.False(state.IsVerified);
        Assert.Equal(46, state.LocalSizeBytes);
        Assert.Contains("segnaposto", state.VerificationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageModelState_ReturnsRemainingDownloadBytes()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-remaining-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000);
        SeedImageModelFile(tempDescriptor, "partial");

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);

        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal(1_000, state.ExpectedSizeBytes);
        Assert.Equal(993, state.RemainingDownloadBytes);
    }

    [Fact]
    public async Task ImageModelState_TreatsSnapshotWithoutShaAsReady()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-snapshot-ready-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);

        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal("Ready", state.State);
        Assert.True(state.IsDownloaded);
        Assert.True(state.IsVerified);
        Assert.Null(state.VerificationError);
    }

    [Fact]
    public async Task ImageModelState_RejectsIncompleteBuiltInSnapshot()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-snapshot-incomplete-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedImageModelSnapshotFile(tempDescriptor, "model_index.json", "{}");
        await SaveImageSettingsAsync(httpClient);

        ImageGenerationRuntimeStatus? runtimeStatus =
            await httpClient.GetFromJsonAsync<ImageGenerationRuntimeStatus>("/api/images/runtime/status", JsonOptions);

        Assert.NotNull(runtimeStatus);
        Assert.False(runtimeStatus.IsReady);
        Assert.Equal("NotDownloaded", runtimeStatus.State);
        Assert.Contains("incompleto", runtimeStatus.Suggestion ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.Conflict,
            "Modello immagini non pronto",
            "image_generation_model_not_ready");
    }

    [Fact]
    public async Task ImageModelState_RejectsUnsupportedSnapshotPipeline()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-snapshot-pipeline-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        SeedImageModelSnapshotFile(tempDescriptor, "model_index.json", """{"_class_name":"StableDiffusionPipeline"}""");

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);

        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal("VerificationFailed", state.State);
        Assert.True(state.IsDownloaded);
        Assert.False(state.IsVerified);
        Assert.Contains("Pipeline modello non supportata", state.VerificationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageModelState_IgnoresStalePlaceholderWhenSnapshotRequiredFilesExist()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-snapshot-placeholder-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedPlaceholderImageModel(tempDescriptor);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);

        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal("Ready", state.State);
        Assert.True(state.IsVerified);
        Assert.DoesNotContain("segnaposto", state.VerificationError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImageGeneration_WithFakeEngine_SavesAndDeletesGeneratedImage()
    {
        FakeImageGenerationEngine engine = new();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-delete-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SeedVerifiedImageModelAsync(httpClient, tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
                "/api/images/generate",
                new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
                JsonOptions);
        ImageGenerationResponse? generated = await generateResponse.Content.ReadFromJsonAsync<ImageGenerationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.NotNull(generated);
        GeneratedImage image = Assert.Single(generated.Images);
        Assert.True(File.Exists(Path.Combine(tempDescriptor.Descriptor.StoragePaths.DataRoot, "images", "generated", image.FileName)));

        using HttpResponseMessage deleteResponse = await httpClient.DeleteAsync($"/api/images/{image.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        Assert.False(File.Exists(Path.Combine(tempDescriptor.Descriptor.StoragePaths.DataRoot, "images", "generated", image.FileName)));

        using HttpResponseMessage fileResponse = await httpClient.GetAsync($"/api/images/{image.Id}/file");
        Assert.Equal(HttpStatusCode.NotFound, fileResponse.StatusCode);
    }

    [Fact]
    public async Task ImageGeneration_WithSnapshotModelWithoutSha_AllowsGeneration()
    {
        FakeImageGenerationEngine engine = new();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-snapshot-generate-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);
        ImageGenerationResponse? generated = await generateResponse.Content.ReadFromJsonAsync<ImageGenerationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.NotNull(generated);
        Assert.Single(generated.Images);
    }

    [Fact]
    public async Task ImageGeneration_EngineMissingModelFileReturnsModelNotReady()
    {
        FakeImageGenerationEngine engine = new()
        {
            ExceptionToThrow = new FileNotFoundException("Onnx model file not found")
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-engine-file-missing-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.Conflict,
            "Modello immagini non pronto",
            "image_generation_model_not_ready");
    }

    [Fact]
    public async Task ImageGeneration_EngineTimeoutReturnsTypedProblem()
    {
        FakeImageGenerationEngine engine = new()
        {
            ExceptionToThrow = new TimeoutException("generation timed out")
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-engine-timeout-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.RequestTimeout,
            "Timeout generazione immagini",
            "image_generation_timeout");
    }

    [Fact]
    public async Task ImageGeneration_EngineConfigurationFailureReturnsTypedProblem()
    {
        FakeImageGenerationEngine engine = new()
        {
            ExceptionToThrow = new InvalidOperationException("Unsupported model layout")
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-engine-config-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.BadRequest,
            "Configurazione immagini non valida",
            "image_generation_invalid_configuration");
    }

    [Fact]
    public async Task ImageGeneration_EngineEmptyOutputReturnsTypedProblem()
    {
        FakeImageGenerationEngine engine = new()
        {
            ReturnEmptyResult = true
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-engine-empty-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await UpsertImageModelCatalogAsync(httpClient, expectedSizeBytes: 1_000, requiredFiles: ["model_index.json"]);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);

        await AssertProblemAsync(
            generateResponse,
            HttpStatusCode.BadGateway,
            "Errore generazione immagini",
            "image_generation_unexpected_response");
    }

    [Fact]
    public async Task ImageGeneration_RuntimeStatusReflectsEngineFallback()
    {
        FakeImageGenerationEngine engine = new()
        {
            ActiveExecutionProvider = "CPU",
            FallbackReason = "DirectML non disponibile per il test."
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-fallback-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SeedVerifiedImageModelAsync(httpClient, tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        _ = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);
        ImageGenerationRuntimeStatus? status =
            await httpClient.GetFromJsonAsync<ImageGenerationRuntimeStatus>("/api/images/runtime/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status.IsReady);
        Assert.Equal("CPU", status.ExecutionProvider);
        Assert.Equal("DirectML", status.PreferredExecutionProvider);
        Assert.Equal("DirectML non disponibile per il test.", status.FallbackReason);
    }

    [Fact]
    public async Task ImageGeneration_RuntimeStatusUsesPreferredProviderBeforeFirstGeneration()
    {
        FakeImageGenerationEngine engine = new()
        {
            ActiveExecutionProvider = "CPU"
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-initial-provider-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SeedVerifiedImageModelAsync(httpClient, tempDescriptor);
        await SaveImageSettingsAsync(httpClient);

        ImageGenerationRuntimeStatus? status =
            await httpClient.GetFromJsonAsync<ImageGenerationRuntimeStatus>("/api/images/runtime/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status.IsReady);
        Assert.Equal("DirectML", status.ExecutionProvider);
        Assert.Equal("DirectML", status.PreferredExecutionProvider);
        Assert.Null(status.FallbackReason);
    }

    [Fact]
    public async Task ImageGeneration_RuntimeStatusExplainsCpuWhenGpuPreferenceDisabled()
    {
        FakeImageGenerationEngine engine = new()
        {
            ActiveExecutionProvider = "CPU"
        };
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-cpu-preference-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ImageGenerationEngine = engine });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SeedVerifiedImageModelAsync(httpClient, tempDescriptor);
        using HttpResponseMessage settingsResponse = await httpClient.PutAsJsonAsync(
            "/api/settings/image-generation",
            new ImageGenerationSettings(
                ImageModelCatalog.DefaultModelId,
                60,
                PreferGpu: false),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        ImageGenerationRuntimeStatus? status =
            await httpClient.GetFromJsonAsync<ImageGenerationRuntimeStatus>("/api/images/runtime/status", JsonOptions);

        Assert.NotNull(status);
        Assert.True(status.IsReady);
        Assert.Equal("CPU", status.ExecutionProvider);
        Assert.Equal("CPU", status.PreferredExecutionProvider);
        Assert.Equal("DirectML disabilitato nelle impostazioni immagini.", status.FallbackReason);
    }

    [Fact]
    public async Task ImageGeneration_SettingsFallsBackFromRemovedCudaModel()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-removed-cuda-model-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteSettingsRepository settings = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store));
        await settings.UpsertAsync("imageGeneration.selectedModelId", "onnxruntime-sdxl-turbo-cuda");
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        ImageGenerationSettings? imageSettings =
            await httpClient.GetFromJsonAsync<ImageGenerationSettings>("/api/settings/image-generation", JsonOptions);

        Assert.NotNull(imageSettings);
        Assert.Equal(ImageModelCatalog.DefaultModelId, imageSettings.SelectedModelId);
        Assert.True(imageSettings.PreferGpu);
    }

    [Fact]
    public async Task ImageGeneration_OpenFolderRequiresConfirmationAndStartsExplorer()
    {
        FakeProcessLauncher processLauncher = new();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-open-folder-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { ProcessLauncher = processLauncher });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage rejected = await httpClient.PostAsJsonAsync(
            "/api/images/open-folder",
            new ProcessLaunchRequest(Confirmed: false),
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using HttpResponseMessage accepted = await httpClient.PostAsJsonAsync(
            "/api/images/open-folder",
            new ProcessLaunchRequest(Confirmed: true),
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        ProcessStartInfo startInfo = Assert.Single(processLauncher.StartedProcesses);
        Assert.Contains(Path.Combine(tempDescriptor.Descriptor.StoragePaths.DataRoot, "images", "generated"), startInfo.ArgumentList);
    }

    private static async Task SaveImageSettingsAsync(HttpClient httpClient)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            "/api/settings/image-generation",
            new ImageGenerationSettings(
                ImageModelCatalog.DefaultModelId,
                60,
                PreferGpu: true),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task SeedVerifiedImageModelAsync(
        HttpClient httpClient,
        TempBackendDescriptor tempDescriptor)
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes("real-test-model");
        SeedImageModelFile(tempDescriptor, content);
        SeedRequiredSdxlSnapshotFiles(tempDescriptor);
        string sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        await UpsertImageModelCatalogAsync(httpClient, content.LongLength, sha256);
    }

    private static void SeedImageModelFile(TempBackendDescriptor tempDescriptor, string content)
    {
        SeedImageModelFile(tempDescriptor, System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static void SeedImageModelFile(TempBackendDescriptor tempDescriptor, byte[] content)
    {
        string modelDirectory = Path.Combine(
            tempDescriptor.Descriptor.StoragePaths.ImageModelsDirectory,
            ImageModelCatalog.DefaultModelId);
        Directory.CreateDirectory(modelDirectory);
        File.WriteAllBytes(Path.Combine(modelDirectory, ImageModelCatalog.RequiredModelFileName), content);
    }

    private static void SeedImageModelSnapshotFile(
        TempBackendDescriptor tempDescriptor,
        string relativePath,
        string content)
    {
        string modelDirectory = Path.Combine(
            tempDescriptor.Descriptor.StoragePaths.ImageModelsDirectory,
            ImageModelCatalog.DefaultModelId);
        string destinationPath = Path.Combine(modelDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, content);
    }

    private static void SeedRequiredSdxlSnapshotFiles(TempBackendDescriptor tempDescriptor)
    {
        foreach (string requiredFile in ImageModelCatalog.RequiredSdxlSnapshotFiles)
        {
            SeedImageModelSnapshotFile(tempDescriptor, requiredFile, "{}");
        }
    }

    private static async Task UpsertImageModelCatalogAsync(
        HttpClient httpClient,
        long expectedSizeBytes,
        string sha256 = "",
        IReadOnlyList<string>? requiredFiles = null)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/images/models/catalog/{ImageModelCatalog.DefaultModelId}",
            new ImageModelCatalogEntryRequest(
                ImageModelCatalog.DefaultModelId,
                "OnlyRag test image model",
                "DirectML GPU consigliato, CPU disponibile per fallback",
                "file:///C:/OnlyRag/test-model.onnx",
                "Test license",
                expectedSizeBytes,
                requiredFiles ?? [ImageModelCatalog.RequiredModelFileName],
                sha256),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task UpsertManualImageModelCatalogAsync(
        HttpClient httpClient,
        string modelId,
        string downloadUrl,
        long expectedSizeBytes,
        string sha256)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            $"/api/images/models/catalog/{modelId}",
            new ImageModelCatalogEntryRequest(
                modelId,
                "Manual incremental test model",
                "CPU test model",
                downloadUrl,
                "Test license",
                expectedSizeBytes,
                [ImageModelCatalog.RequiredModelFileName],
                sha256),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string GetModelFilePath(TempBackendDescriptor tempDescriptor, string modelId)
    {
        return Path.Combine(
            tempDescriptor.Descriptor.StoragePaths.ImageModelsDirectory,
            modelId,
            ImageModelCatalog.RequiredModelFileName);
    }

    private static void SeedPlaceholderImageModel(TempBackendDescriptor tempDescriptor)
    {
        string modelDirectory = Path.Combine(
            tempDescriptor.Descriptor.StoragePaths.ImageModelsDirectory,
            ImageModelCatalog.DefaultModelId);
        Directory.CreateDirectory(modelDirectory);
        File.WriteAllBytes(
            Path.Combine(modelDirectory, ImageModelCatalog.RequiredModelFileName),
            System.Text.Encoding.UTF8.GetBytes(ImageModelCatalog.PlaceholderModelContent));
    }

    private sealed class FakeImageGenerationEngine : IImageGenerationEngine
    {
        public string ActiveExecutionProvider { get; init; } = "DirectML";

        public string? FallbackReason { get; init; }

        public Exception? ExceptionToThrow { get; init; }

        public bool ReturnEmptyResult { get; init; }

        private bool hasGenerated;

        public ImageGenerationEngineStatus GetStatus() =>
            new(ActiveExecutionProvider, FallbackReason, hasGenerated);

        public Task<ImageGenerationEngineResult> GenerateAsync(
            ImageGenerationRequest request,
            string modelDirectory,
            bool preferGpu,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            hasGenerated = true;
            return Task.FromResult(new ImageGenerationEngineResult(
                ReturnEmptyResult ? [] : [new ImageGenerationBinary(CreateTinyPng(), "image/png", ".png")],
                ActiveExecutionProvider,
                FallbackReason));
        }

        private static byte[] CreateTinyPng() =>
        [
            137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
            0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 31, 21, 196, 137,
            0, 0, 0, 13, 73, 68, 65, 84, 120, 156, 99, 248, 15, 4, 0,
            9, 251, 3, 253, 167, 181, 60, 199, 0, 0, 0, 0, 73, 69, 78,
            68, 174, 66, 96, 130
        ];
    }
}
