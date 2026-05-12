using Microsoft.AspNetCore.Builder;

namespace OnlyRag.Api;

public sealed class InProcessBackendHandle : IAsyncDisposable
{
    private readonly WebApplication application;

    internal InProcessBackendHandle(
        WebApplication application,
        Uri baseUri,
        InProcessBackendDescriptor descriptor,
        string sessionToken)
    {
        this.application = application;
        BaseUri = baseUri;
        Descriptor = descriptor;
        SessionToken = sessionToken;
    }

    public Uri BaseUri { get; }

    public InProcessBackendDescriptor Descriptor { get; }

    public string SessionToken { get; }

    public CancellationToken StoppedToken => application.Lifetime.ApplicationStopped;

    public async ValueTask DisposeAsync()
    {
        BackendLog.Write(Descriptor.StoragePaths, $"Stopping in-process backend at {BaseUri}.");
        using CancellationTokenSource shutdownTimeout = new(TimeSpan.FromSeconds(15));
        try
        {
            await Task.Run(
                () => application.StopAsync(shutdownTimeout.Token),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            BackendLog.Write(Descriptor.StoragePaths, "In-process backend stop timed out; disposing host.");
        }
        finally
        {
            await Task.Run(
                async () => await application.DisposeAsync().ConfigureAwait(false),
                CancellationToken.None).ConfigureAwait(false);
        }

        BackendLog.Write(Descriptor.StoragePaths, "In-process backend stopped.");
    }
}
