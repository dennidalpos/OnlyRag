using System.Net;
using System.Net.Http.Json;
using OnlyRag.Core;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task ImageGeneration_Automatic1111GeneratesAndServesLocalFile()
    {
        await using FakeImageGenerationServer imageServer = await FakeImageGenerationServer.StartAsync();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-a1111-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SaveImageSettingsAsync(httpClient, imageServer.BaseUrl, imageServer.BaseUrl, "automatic1111");

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("automatic1111", "A local-first document desk", null, null, 512, 512, 8, 1, 42),
            JsonOptions);
        ImageGenerationResponse? payload = await generateResponse.Content.ReadFromJsonAsync<ImageGenerationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.NotNull(payload);
        GeneratedImage image = Assert.Single(payload.Images);
        Assert.Equal("automatic1111", image.Provider);
        Assert.Equal("A local-first document desk", image.Prompt);
        Assert.Equal("image/png", image.MimeType);

        GeneratedImage[]? listed = await httpClient.GetFromJsonAsync<GeneratedImage[]>("/api/images", JsonOptions);
        Assert.NotNull(listed);
        GeneratedImage listedImage = Assert.Single(listed);
        Assert.Equal(image.Id, listedImage.Id);

        using HttpResponseMessage fileResponse = await httpClient.GetAsync($"/api/images/{image.Id}/file");
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal("image/png", fileResponse.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await fileResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ImageGeneration_ComfyUiGeneratesWithDefaultWorkflow()
    {
        await using FakeImageGenerationServer imageServer = await FakeImageGenerationServer.StartAsync();
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-comfy-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        await SaveImageSettingsAsync(httpClient, imageServer.BaseUrl, imageServer.BaseUrl, "comfyui");

        using HttpResponseMessage generateResponse = await httpClient.PostAsJsonAsync(
            "/api/images/generate",
            new ImageGenerationRequest("comfyui", "A glass archive", null, "test-model.safetensors", 512, 512, 8, 1, 42),
            JsonOptions);
        ImageGenerationResponse? payload = await generateResponse.Content.ReadFromJsonAsync<ImageGenerationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        Assert.NotNull(payload);
        GeneratedImage image = Assert.Single(payload.Images);
        Assert.Equal("comfyui", image.Provider);
        Assert.Equal("A glass archive", image.Prompt);
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public async Task ImageGenerationSettings_RejectsRemoteEndpointWithoutTrust()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("image-remote-settings-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            "/api/settings/image-generation",
            new ImageGenerationSettings(
                "automatic1111",
                "http://192.0.2.10:7860",
                "http://127.0.0.1:8188",
                300,
                TrustNonLocalEndpoint: false,
                Automatic1111Model: null,
                ComfyUiWorkflowJson: null),
            JsonOptions);

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Configurazione immagini non valida",
            "image_generation_invalid_configuration");
    }

    private static async Task SaveImageSettingsAsync(
        HttpClient httpClient,
        string automatic1111BaseUrl,
        string comfyUiBaseUrl,
        string provider)
    {
        using HttpResponseMessage response = await httpClient.PutAsJsonAsync(
            "/api/settings/image-generation",
            new ImageGenerationSettings(
                provider,
                automatic1111BaseUrl,
                comfyUiBaseUrl,
                60,
                TrustNonLocalEndpoint: false,
                Automatic1111Model: null,
                ComfyUiWorkflowJson: null),
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

