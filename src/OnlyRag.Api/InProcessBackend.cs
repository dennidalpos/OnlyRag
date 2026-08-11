using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OnlyRag.Core;
using OnlyRag.Core.Logging;
using OnlyRag.Infrastructure.Ingestion;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Api;

public static partial class InProcessBackend
{
    public static async Task<InProcessBackendHandle> StartAsync(
        InProcessBackendDescriptor? descriptor = null,
        InProcessBackendOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var startBackendScope = EarlyBootstrapperLogger.TraceScope("InProcessBackend_StartAsync");

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

        WebApplication app;
        using (EarlyBootstrapperLogger.TraceScope("Build_WebApplication_DI"))
        {
            app = BuildApplication(descriptor, options, runtimeState, sessionToken);
        }

        // StartupTracer is available after DI container is built
        StartupTracer startupTracer = app.Services.GetRequiredService<StartupTracer>();
        startupTracer.Record("Backend: DI container built");
        EarlyBootstrapperLogger.LogMilestone("Backend_DI_Built", "DI Container built successfully.");

        var loggingService = app.Services.GetService<OnlyRag.Infrastructure.Logging.ILoggingService>();
        if (loggingService is not null)
        {
            BackendLog.SetLoggingService(loggingService);
        }

        try
        {
            StorageStatusResponse storageStatus;
            using (EarlyBootstrapperLogger.TraceScope("Initialize_SQLite_Storage"))
            {
                storageStatus = await app.Services
                    .GetRequiredService<ILocalStorageService>()
                    .InitializeAsync(cancellationToken);
            }
            runtimeState.DatabaseStatus = storageStatus.SchemaStatus;
            BackendLog.Write(descriptor.StoragePaths, $"Local SQLite schema version {storageStatus.CurrentSchemaVersion}/{storageStatus.TargetSchemaVersion}: {storageStatus.SchemaStatus}.");
            startupTracer.Record($"SQLite: Schema v{storageStatus.CurrentSchemaVersion}/{storageStatus.TargetSchemaVersion} ({storageStatus.SchemaStatus})");

            startupTracer.Record("Qdrant: Local runtime initialization scheduled");

            int recoveredJobs = await app.Services
                .GetRequiredService<ILocalJobQueue>()
                .RecoverInterruptedJobsAsync(cancellationToken);
            if (recoveredJobs > 0)
            {
                BackendLog.Write(descriptor.StoragePaths, $"Recovered {recoveredJobs} interrupted job(s).");
                startupTracer.Record($"Worker: Recovered {recoveredJobs} interrupted job(s)");
            }
            else
            {
                startupTracer.Record("Worker: Job queue ready (0 interrupted)");
            }

            using (EarlyBootstrapperLogger.TraceScope("Start_Kestrel_Server"))
            {
                await app.StartAsync(cancellationToken);
            }
            Uri baseUri = ResolveBaseUri(app);
            runtimeState.BaseUri = baseUri;
            BackendLog.Write(descriptor.StoragePaths, $"In-process backend listening on {baseUri}.");
            startupTracer.Record($"Kestrel: HTTP server listening on {baseUri}");

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

}
