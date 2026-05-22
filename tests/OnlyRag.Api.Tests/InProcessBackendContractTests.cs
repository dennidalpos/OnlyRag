using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task ApiContracts_UnauthorizedRequestsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.GetAsync("/api/documents");

        await AssertProblemAsync(
            response,
            HttpStatusCode.Unauthorized,
            "Non autorizzato",
            "unauthorized");
    }

    [Fact]
    public async Task ApiContracts_NotFoundRequestsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.GetAsync("/api/documents/999999");

        await AssertProblemAsync(
            response,
            HttpStatusCode.NotFound,
            "Documento non trovato",
            "not_found");
    }

    [Fact]
    public async Task ApiContracts_ValidationErrorsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/search",
            new DocumentSearchRequest("", [1], TopK: null));

        await AssertProblemAsync(
            response,
            HttpStatusCode.BadRequest,
            "Query non valida",
            "search_query_required");
    }

    [Fact]
    public async Task ApiContracts_ConflictErrorsReturnProblemDetails()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("contract-conflict-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("not-indexed")), "files", "NotIndexed.txt");
        using HttpResponseMessage importResponse = await httpClient.PostAsync("/api/documents/import", content);
        DocumentImportResponse? importPayload = await importResponse.Content.ReadFromJsonAsync<DocumentImportResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);
        Assert.NotNull(importPayload);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/translations",
            new CreateTranslationRequest(importPayload.Documents[0].Document.Id, "English", "gemma3:4b"),
            JsonOptions);

        await AssertProblemAsync(
            response,
            HttpStatusCode.Conflict,
            "Documento non indicizzato",
            "document_not_indexed");
    }

    [Fact]
    public async Task ApiContracts_DocumentImportResponseUsesSharedDtoShape()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("contract-import-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);
        using MultipartFormDataContent content = new();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("contract-document")), "files", "Contract.txt");

        using HttpResponseMessage response = await httpClient.PostAsync("/api/documents/import", content);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement result = json.RootElement.GetProperty("documents")[0];
        JsonElement document = result.GetProperty("document");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(result.GetProperty("deduplicated").GetBoolean());
        Assert.Equal("Contract.txt", document.GetProperty("originalFileName").GetString());
        Assert.Equal("Queued", document.GetProperty("status").GetString());
        Assert.True(document.TryGetProperty("currentJobId", out _));
        Assert.False(document.TryGetProperty("CurrentJobId", out _));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedTitle,
        string expectedCode)
    {
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument json = JsonDocument.Parse(body);
        JsonElement root = json.RootElement;

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((int)expectedStatus, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
    }
}
