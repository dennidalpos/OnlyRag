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
        Assert.EndsWith(Path.Combine("documents", "originals"), paths.DocumentOriginalsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "renders"), paths.DocumentRendersDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "ocr-cache"), paths.DocumentOcrCacheDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("documents", "exports"), paths.DocumentExportsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("backups", paths.BackupsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("logs", paths.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("webview2", paths.WebView2UserDataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("temp", paths.TempDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(paths.BackupsDirectory, paths.EnumerateRequiredDirectories());
        Assert.Contains(paths.WebView2UserDataDirectory, paths.EnumerateRequiredDirectories());
        Assert.Contains(paths.TempDirectory, paths.EnumerateRequiredDirectories());
    }

    [Fact]
    public void FromRoot_NormalizesRootToFullPath()
    {
        string relativeRoot = Path.Combine(".", "OnlyRag.TestData");

        AppStoragePaths paths = AppStoragePaths.FromRoot(relativeRoot);

        Assert.Equal(Path.GetFullPath(relativeRoot), paths.DataRoot);
        Assert.StartsWith(paths.DataRoot, paths.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.DataRoot, paths.BackupsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.DataRoot, paths.LogsDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.DataRoot, paths.WebView2UserDataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.DataRoot, paths.TempDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(paths.DataRoot, paths.PendingResetMarkerPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromRoot_RejectsMissingRoot()
    {
        Assert.Throws<ArgumentException>(() => AppStoragePaths.FromRoot(" "));
    }

    [Fact]
    public void AppDataReset_RequestAndApplyPendingResetCreatesBackupBeforeDeletingDataRootContents()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-reset-tests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);
        Directory.CreateDirectory(paths.DataDirectory);
        string dataPath = Path.Combine(paths.DataDirectory, "data.txt");
        File.WriteAllText(dataPath, "data");

        AppDataReset.RequestResetOnNextStartup(paths);
        bool applied = AppDataReset.ApplyPendingReset(paths);

        Assert.True(applied);
        Assert.True(Directory.Exists(paths.DataRoot));
        string backupPath = Assert.Single(Directory.EnumerateDirectories(paths.BackupsDirectory));
        Assert.StartsWith("reset-", Path.GetFileName(backupPath), StringComparison.Ordinal);
        Assert.Equal("data", File.ReadAllText(Path.Combine(backupPath, "data", "data.txt")));
        Assert.Equal([paths.BackupsDirectory], Directory.EnumerateFileSystemEntries(paths.DataRoot).ToArray());

        Directory.Delete(paths.DataRoot, recursive: true);
    }
}
