using Microsoft.AspNetCore.Builder;

namespace OnlyRag.Api;

public sealed class InProcessBackendHandle : IAsyncDisposable
{
    private readonly WebApplication application;

    internal InProcessBackendHandle(
        WebApplication application,
        Uri baseUri,
        InProcessBackendDescriptor descriptor)
    {
        this.application = application;
        BaseUri = baseUri;
        Descriptor = descriptor;
    }

    public Uri BaseUri { get; }

    public InProcessBackendDescriptor Descriptor { get; }

    public CancellationToken StoppedToken => application.Lifetime.ApplicationStopped;

    public async ValueTask DisposeAsync()
    {
        BackendLog.Write(Descriptor.StoragePaths, $"Stopping in-process backend at {BaseUri}.");
        using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(15));
        try
        {
            await application.StopAsync(shutdownTimeout.Token);
        }
        catch (OperationCanceledException)
        {
            BackendLog.Write(Descriptor.StoragePaths, "In-process backend stop timed out; disposing host.");
        }
        finally
        {
            await application.DisposeAsync();
        }

        BackendLog.Write(Descriptor.StoragePaths, "In-process backend stopped.");
    }
}
