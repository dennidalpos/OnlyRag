using System.Net;
using System.Net.Http.Json;
using OnlyRag.Core;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task AgentApproveTool_ReturnsBadRequest_WhenCallIdIsEmpty()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/agent/approve-tool",
            new ApproveToolCallRequest("", true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgentApproveTool_ReturnsOk_WhenCallIdProvided()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/agent/approve-tool",
            new ApproveToolCallRequest("call_123", true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", json);
    }

    [Fact]
    public async Task AgentRunStream_ReturnsBadRequest_WhenGoalIsEmpty()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
            "/api/agent/run-stream",
            new AgentRunRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
