using Microsoft.Extensions.Hosting;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

internal sealed class QdrantStartupService(
    QdrantLocalRuntimeService runtime,
    IQdrantVectorStore vectorStore,
    InProcessBackendDescriptor descriptor,
    StartupTracer startupTracer) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            QdrantStatusResponse status = await runtime
                .EnsureLocalServerAsync(vectorStore, stoppingToken)
                .ConfigureAwait(false);

            startupTracer.Record(
                status.IsReachable
                    ? "Qdrant: Local runtime ready"
                    : $"Qdrant: Local runtime unavailable ({status.Error ?? status.Warning ?? status.Status})");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or Grpc.Core.RpcException)
        {
            BackendLog.WriteException(descriptor.StoragePaths, null, "Qdrant local runtime startup failed.", ex);
            startupTracer.Record($"Qdrant: Local runtime startup failed ({ex.Message})");
        }
    }
}
