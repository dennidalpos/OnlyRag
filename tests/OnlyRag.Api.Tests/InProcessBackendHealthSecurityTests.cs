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
    public async Task Health_ReturnsHealthyFromLoopbackDynamicPort()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);

        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(backend.BaseUri.Host)));
        Assert.NotEqual(0, backend.BaseUri.Port);

        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        BackendHealthResponse? response = await httpClient.GetFromJsonAsync<BackendHealthResponse>("/health");

        Assert.NotNull(response);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    public async Task Health_DoesNotRequireSessionToken()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_RejectsMissingSessionToken()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };

        using HttpResponseMessage response = await httpClient.GetAsync("/api/app/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_RejectsWrongSessionToken()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = new()
        {
            BaseAddress = backend.BaseUri
        };
        httpClient.DefaultRequestHeaders.Add(OnlyRagApiHeaders.SessionTokenHeaderName, "wrong-token");

        using HttpResponseMessage response = await httpClient.GetAsync("/api/app/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_AcceptsValidSessionToken()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.GetAsync("/api/app/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisposeAsync_CompletesWhenCallerBlocksWithSynchronizationContext()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(
            new LocalJobQueueDescriptor("dispose-sync-context-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SynchronizationContext? previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            Task disposeTask = backend.DisposeAsync().AsTask();

#pragma warning disable xUnit1031 // This regression test intentionally blocks like WPF OnExit.
            Assert.True(disposeTask.Wait(TimeSpan.FromSeconds(5)));
            disposeTask.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public async Task AppStatus_AllowsStaticWebViewOrigin()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/app/status");
        request.Headers.Add("Origin", OnlyRagWebOrigins.StaticWebViewOrigin);

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        AppStatusResponse? status = await response.Content.ReadFromJsonAsync<AppStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(OnlyRagWebOrigins.StaticWebViewOrigin, GetAccessControlAllowOrigin(response));
        Assert.NotNull(status);
        Assert.Equal("Running", status.Backend);
    }

    [Fact]
    public async Task AppStatus_DoesNotAllowNullOrigin()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/app/status");
        request.Headers.Add("Origin", "null");

        using HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(GetAccessControlAllowOrigin(response));
    }

    [Fact]
    public async Task AppStatus_AllowsDevelopmentOriginWhenEnabled()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { EnableDevelopmentCorsOrigins = true });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/app/status");
        request.Headers.Add("Origin", "http://localhost:5173");

        using HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:5173", GetAccessControlAllowOrigin(response));
    }

    [Fact]
    public async Task AppStatus_DoesNotAllowDevelopmentOriginWhenDisabled()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(
            tempDescriptor.Descriptor,
            new InProcessBackendOptions { EnableDevelopmentCorsOrigins = false });
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/app/status");
        request.Headers.Add("Origin", "http://localhost:5173");

        using HttpResponseMessage response = await httpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(GetAccessControlAllowOrigin(response));
    }

    [Fact]
    public async Task StorageStatus_ReturnsDatabasePathAndMigrationState()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        StorageStatusResponse? status = await httpClient.GetFromJsonAsync<StorageStatusResponse>("/api/app/storage-status");

        Assert.NotNull(status);
        Assert.Equal(tempDescriptor.Descriptor.StoragePaths.DatabasePath, status.DatabasePath);
        Assert.Equal("Current", status.MigrationStatus);
        Assert.Equal(status.TargetSchemaVersion, status.CurrentSchemaVersion);
    }
}

