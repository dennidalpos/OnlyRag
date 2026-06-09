using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Tests;

public sealed class LocalRuntimeDirectoryPreparerTests
{
    [Fact]
    public async Task InitializeAsync_WhenRuntimeDirectoryIsAFile_ReturnsActionablePathError()
    {
        string parent = Path.Combine(Path.GetTempPath(), "OnlyRag.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        string fileRoot = Path.Combine(parent, "runtime-root");
        File.WriteAllText(fileRoot, "not a directory");

        try
        {
            AppStoragePaths paths = AppStoragePaths.FromRoot(fileRoot);
            LocalSqliteStoreDescriptor descriptor = new(paths);
            LocalSqliteConnectionFactory connectionFactory = new(descriptor);
            LocalSqliteStorageService storage = new(descriptor, new LocalSqliteSchemaInitializer(descriptor, connectionFactory));

            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => storage.InitializeAsync());

            Assert.Contains("directory runtime", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(fileRoot, ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("permessi", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }
}
