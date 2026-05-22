using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalSqliteStorageService : ILocalStorageService
{
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly LocalSqliteMigrator migrator;

    public LocalSqliteStorageService(
        LocalSqliteStoreDescriptor descriptor,
        LocalSqliteMigrator migrator)
    {
        this.descriptor = descriptor;
        this.migrator = migrator;
    }

    public async Task<StorageStatusResponse> InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (string directory in descriptor.Paths.EnumerateRequiredDirectories())
        {
            LocalRuntimeDirectoryPreparer.EnsureDirectory(directory);
        }

        return await migrator.MigrateAsync(cancellationToken);
    }

    public Task<StorageStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return migrator.GetStatusAsync(cancellationToken);
    }
}
