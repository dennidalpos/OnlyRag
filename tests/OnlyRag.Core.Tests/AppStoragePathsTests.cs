using OnlyRag.Core;

namespace OnlyRag.Core.Tests;

public sealed class AppStoragePathsTests
{
    [Fact]
    public void FromLocalAppData_UsesOnlyRagDataRoot()
    {
        var paths = AppStoragePaths.FromLocalAppData();

        Assert.EndsWith(AppStoragePaths.ProductName, paths.DataRoot, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("data", "onlyrag.db"), paths.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("data", "jobs.db"), paths.JobQueueDatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "originals"), paths.DocumentOriginalsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "renders"), paths.DocumentRendersDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "ocr-cache"), paths.DocumentOcrCacheDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "exports"), paths.DocumentExportsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("logs", paths.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("temp", paths.TempDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(paths.TempDirectory, paths.EnumerateRequiredDirectories());
    }
}
