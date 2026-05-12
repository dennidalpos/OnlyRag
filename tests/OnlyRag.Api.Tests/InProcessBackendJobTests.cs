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
    public async Task Jobs_ReturnsPersistentQueue()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        LocalJob[]? jobs = await httpClient.GetFromJsonAsync<LocalJob[]>("/api/jobs");

        Assert.NotNull(jobs);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task PrepareShutdown_WithNoActiveJobs_ReturnsCompleteResponse()
    {
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(new LocalJobQueueDescriptor("shutdown-empty-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0));
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        AppShutdownPreparationResponse? payload = await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.IsComplete);
        Assert.Equal(0, payload.ActiveJobCount);
        Assert.Equal(0, payload.CancelledJobCount);
        Assert.Empty(payload.UnstoppedJobIds);
    }

    [Fact]
    public async Task PrepareShutdown_CancelsPendingPausedAndRunningJobs()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-active-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob runningSeed = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob? running = await queue.TryLeaseNextAsync();
        Assert.NotNull(running);
        Assert.Equal(runningSeed.Id, running.Id);
        LocalJob pending = await queue.CreateAsync(new CreateLocalJobRequest("pending-test", "{}"));
        LocalJob paused = await queue.CreateAsync(new CreateLocalJobRequest("paused-test", "{}"));
        await queue.PauseAsync(paused.Id);
        using HttpClient httpClient = CreateAuthenticatedClient(backend);

        using HttpResponseMessage response = await httpClient.PostAsync("/api/app/prepare-shutdown", content: null);
        AppShutdownPreparationResponse? payload = await response.Content.ReadFromJsonAsync<AppShutdownPreparationResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.True(payload.IsComplete);
        Assert.Equal(3, payload.ActiveJobCount);
        Assert.Equal(3, payload.CancelledJobCount);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(pending.Id))!.Status);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(paused.Id))!.Status);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(running.Id))!.Status);
    }

    [Fact]
    public async Task PrepareShutdown_WaitsForRegisteredRunningJobToStop()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-registry-stop-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob running = (await queue.TryLeaseNextAsync())!;
        RunningJobCancellationRegistry registry = new();
        CancellationTokenSource jobCancellation = registry.Register(created.Id, CancellationToken.None);
        ApplicationShutdownService shutdown = new(queue, registry, tempDescriptor.Descriptor);
        Task unregisterOnCancel = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, jobCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                registry.Unregister(created.Id);
            }
        });

        AppShutdownPreparationResponse payload = await shutdown.PrepareAsync(TimeSpan.FromSeconds(5));
        await unregisterOnCancel;

        Assert.Equal(running.Id, created.Id);
        Assert.True(payload.IsComplete);
        Assert.Empty(payload.UnstoppedJobIds);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(created.Id))!.Status);
    }

    [Fact]
    public async Task PrepareShutdown_ReturnsUnstoppedRegisteredRunningJobsAfterTimeout()
    {
        LocalJobQueueDescriptor queueDescriptor = new("shutdown-registry-timeout-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0);
        using TempBackendDescriptor tempDescriptor = TempBackendDescriptor.Create(queueDescriptor);
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(tempDescriptor.Descriptor);
        SqliteLocalJobQueue queue = new(new LocalSqliteConnectionFactory(tempDescriptor.Descriptor.Store), queueDescriptor);
        LocalJob created = await queue.CreateAsync(new CreateLocalJobRequest("running-test", "{}"));
        LocalJob running = (await queue.TryLeaseNextAsync())!;
        RunningJobCancellationRegistry registry = new();
        registry.Register(created.Id, CancellationToken.None);
        ApplicationShutdownService shutdown = new(queue, registry, tempDescriptor.Descriptor);

        AppShutdownPreparationResponse payload = await shutdown.PrepareAsync(TimeSpan.FromMilliseconds(120));
        registry.Unregister(created.Id);

        Assert.Equal(running.Id, created.Id);
        Assert.False(payload.IsComplete);
        Assert.Equal([created.Id], payload.UnstoppedJobIds);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(created.Id))!.Status);
    }
}

