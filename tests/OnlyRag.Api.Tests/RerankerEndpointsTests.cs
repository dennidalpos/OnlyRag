using System.Net;
using System.Net.Http.Json;
using OnlyRag.Api;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task GetRerankerModelStatus_ReturnsOkAndModelInfo()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient client = CreateAuthenticatedClient(backend);

        HttpResponseMessage response = await client.GetAsync("/api/rag/reranker/model");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RerankerModelInfo? info = await response.Content.ReadFromJsonAsync<RerankerModelInfo>();
        Assert.NotNull(info);
        Assert.Equal(RerankerModelManager.DefaultModelId, info.Id);
        Assert.False(info.IsDownloaded);
    }

    [Fact]
    public async Task DeleteRerankerModel_ReturnsOk()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient client = CreateAuthenticatedClient(backend);

        HttpResponseMessage response = await client.DeleteAsync("/api/rag/reranker/model");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelRerankerDownload_ReturnsOk()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient client = CreateAuthenticatedClient(backend);

        HttpResponseMessage response = await client.DeleteAsync("/api/rag/reranker/download");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
