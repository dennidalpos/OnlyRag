using System.Net;
using System.Net.Http.Json;
using OnlyRag.Api.Images;
using OnlyRag.Core;
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
        Assert.True(catalog.Length >= 3);
        ImageModelCatalogEntry model = Assert.Single(catalog, candidate => candidate.Id == ImageModelCatalog.DefaultModelId);
        Assert.Equal(ImageModelCatalog.DefaultModelId, model.Id);
        Assert.True(model.IsBuiltIn);
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
    public async Task ImageGeneration_PlaceholderModelReturnsClearError()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-integrated-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SeedPlaceholderImageModel(tempDescriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
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

        ImageModelLocalState[]? states = await httpClient.GetFromJsonAsync<ImageModelLocalState[]>("/api/images/models", JsonOptions);
        Assert.NotNull(states);
        ImageModelLocalState state = Assert.Single(states, candidate => candidate.ModelId == ImageModelCatalog.DefaultModelId);
        Assert.Equal("VerificationFailed", state.State);
        Assert.False(state.IsVerified);
        Assert.Equal(46, state.LocalSizeBytes);
        Assert.Contains("segnaposto", state.VerificationError, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SaveImageSettingsAsync(HttpClient httpClient)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            "/api/settings/image-generation",
            new ImageGenerationSettings(
                ImageModelCatalog.DefaultModelId,
                60,
                PreferGpu: true,
                ActiveExecutionProvider: "CPU"),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
}
