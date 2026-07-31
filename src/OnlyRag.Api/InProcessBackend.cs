using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;
using OnlyRag.Worker;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    public static async Task<InProcessBackendHandle> StartAsync(
        InProcessBackendDescriptor? descriptor = null,
        InProcessBackendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        descriptor ??= InProcessBackendDescriptor.CreateDefault();
        options ??= new InProcessBackendOptions();

        if (!IPAddress.IsLoopback(options.Address))
        {
            throw new InvalidOperationException("OnlyRag in-process backend can only bind to a loopback address.");
        }

        PrepareDataRoot(descriptor.StoragePaths.DataRoot);
        BackendLog.Write(descriptor.StoragePaths, "Starting in-process backend.");

        string sessionToken = ResolveSessionToken(options);
        var runtimeState = new BackendRuntimeState(DateTimeOffset.UtcNow);
        WebApplication app = BuildApplication(descriptor, options, runtimeState, sessionToken);

        try
        {
            StorageStatusResponse storageStatus = await app.Services
                .GetRequiredService<ILocalStorageService>()
                .InitializeAsync(cancellationToken);
            runtimeState.DatabaseStatus = storageStatus.SchemaStatus;
            BackendLog.Write(descriptor.StoragePaths, $"Local SQLite schema version {storageStatus.CurrentSchemaVersion}/{storageStatus.TargetSchemaVersion}: {storageStatus.SchemaStatus}.");

            int recoveredJobs = await app.Services
                .GetRequiredService<ILocalJobQueue>()
                .RecoverInterruptedJobsAsync(cancellationToken);
            if (recoveredJobs > 0)
            {
                BackendLog.Write(descriptor.StoragePaths, $"Recovered {recoveredJobs} interrupted job(s).");
            }

            await app.StartAsync(cancellationToken);
            Uri baseUri = ResolveBaseUri(app);
            runtimeState.BaseUri = baseUri;
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend listening on {baseUri}.");
            await EnsureQdrantLocalRuntimeAsync(app, descriptor, cancellationToken);

            return new InProcessBackendHandle(app, baseUri, descriptor, sessionToken);
        }
        catch (Exception ex)
        {
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend failed to start: {ex.Message}");
            await app.DisposeAsync();
            throw;
        }
    }

    private static void PrepareDataRoot(string dataRoot)
    {
        try
        {
            Directory.CreateDirectory(dataRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                "Unable to prepare the main OnlyRag runtime directory. " +
                $"Path: {dataRoot}. " +
                "Verify that the path is not a file and that the current user has read and write permissions.",
                ex);
        }
    }

    private static async Task EnsureQdrantLocalRuntimeAsync(
        WebApplication app,
        InProcessBackendDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        try
        {
            QdrantStatusResponse status = await app.Services
                .GetRequiredService<QdrantLocalRuntimeService>()
                .EnsureLocalServerAsync(
                    app.Services.GetRequiredService<IQdrantVectorStore>(),
                    cancellationToken);

            if (!status.IsReachable)
            {
                BackendLog.Write(descriptor.StoragePaths, $"Qdrant local runtime unavailable: {status.Error ?? status.Warning ?? status.Status}");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or Grpc.Core.RpcException)
        {
            BackendLog.WriteException(descriptor.StoragePaths, null, "Qdrant local runtime startup failed.", ex);
        }
    }
}

