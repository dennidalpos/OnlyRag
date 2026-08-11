using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteStorageService : ILocalStorageService
{
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly LocalSqliteSchemaInitializer schemaInitializer;

    public LocalSqliteStorageService(
        LocalSqliteStoreDescriptor descriptor,
        LocalSqliteSchemaInitializer schemaInitializer)
    {
        this.descriptor = descriptor;
        this.schemaInitializer = schemaInitializer;
    }

    public async Task<StorageStatusResponse> InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (string directory in descriptor.Paths.EnumerateRequiredDirectories())
        {
            LocalRuntimeDirectoryPreparer.EnsureDirectory(directory);
        }

        return await schemaInitializer.InitializeAsync(cancellationToken);
    }

    public Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return schemaInitializer.GetStatusAsync(cancellationToken);
    }
}
