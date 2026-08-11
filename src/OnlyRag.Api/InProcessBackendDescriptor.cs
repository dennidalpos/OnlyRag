using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Jobs.Abstractions;

namespace OnlyRag.Api;

public sealed record InProcessBackendDescriptor(
    AppStoragePaths StoragePaths,
    LocalSqliteStoreDescriptor Store,
    LocalJobQueueDescriptor JobQueue,
    OllamaEndpointOptions Ollama)
{
    public static InProcessBackendDescriptor CreateDefault()
    {
        var storagePaths = AppStoragePaths.FromLocalAppData();

        return new InProcessBackendDescriptor(
            storagePaths,
            new LocalSqliteStoreDescriptor(storagePaths),
            LocalJobQueueDescriptor.Default,
            new OllamaEndpointOptions());
    }
}
