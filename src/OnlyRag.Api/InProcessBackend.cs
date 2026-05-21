using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
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

        Directory.CreateDirectory(descriptor.StoragePaths.DataRoot);
        BackendLog.Write(descriptor.StoragePaths, "Starting in-process backend.");

        string sessionToken = ResolveSessionToken(options);
        var runtimeState = new BackendRuntimeState(DateTimeOffset.UtcNow);
        WebApplication app = BuildApplication(descriptor, options, runtimeState, sessionToken);

        try
        {
            StorageStatusResponse storageStatus = await app.Services
                .GetRequiredService<ILocalStorageService>()
                .InitializeAsync(cancellationToken);
            runtimeState.DatabaseStatus = storageStatus.MigrationStatus;
            BackendLog.Write(descriptor.StoragePaths, $"Local SQLite schema version {storageStatus.CurrentSchemaVersion}/{storageStatus.TargetSchemaVersion}: {storageStatus.MigrationStatus}.");

            int recoveredJobs = await app.Services
                .GetRequiredService<ILocalJobQueue>()
                .RecoverInterruptedJobsAsync(cancellationToken);
            if (recoveredJobs > 0)
            {
                BackendLog.Write(descriptor.StoragePaths, $"Recovered {recoveredJobs} interrupted job(s).");
            }

            await app.Services
                .GetRequiredService<SqliteVecVectorSearchService>()
                .VerifyAvailabilityAsync(cancellationToken);
            BackendLog.Write(descriptor.StoragePaths, "sqlite-vec native extension verified.");

            await app.StartAsync(cancellationToken);
            Uri baseUri = ResolveBaseUri(app);
            runtimeState.BaseUri = baseUri;
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend listening on {baseUri}.");

            return new InProcessBackendHandle(app, baseUri, descriptor, sessionToken);
        }
        catch (Exception ex)
        {
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend failed to start: {ex.Message}");
            await app.DisposeAsync();
            throw;
        }
    }
}

