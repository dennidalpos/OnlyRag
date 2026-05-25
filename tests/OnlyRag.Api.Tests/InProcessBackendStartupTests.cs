using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed partial class InProcessBackendTests
{
    [Fact]
    public async Task StartAsync_WhenDataRootIsAFile_ReturnsActionableRuntimePathError()
    {
        string parent = Path.Combine(Path.GetTempPath(), "OnlyRag.Api.Startup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        string fileRoot = Path.Combine(parent, "runtime-root");
        await File.WriteAllTextAsync(fileRoot, "not a directory");

        try
        {
            AppStoragePaths paths = AppStoragePaths.FromRoot(fileRoot);
            InProcessBackendDescriptor descriptor = new(
                paths,
                new LocalSqliteStoreDescriptor(paths),
                LocalJobQueueDescriptor.Default,
                new OllamaEndpointOptions());

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => InProcessBackend.StartAsync(descriptor));

            Assert.Contains("directory runtime principale", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(fileRoot, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("permessi", error.Message, StringComparison.OrdinalIgnoreCase);
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
