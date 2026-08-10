using System.Security.Cryptography;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Update;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SelectiveUpdateManagerTests
{
    [Fact]
    public async Task ApplyAsync_UpdatesManifestFilesAndPreservesLocalDataAndModels()
    {
        string root = CreateRoot();
        try
        {
            string release = Path.Combine(root, "release");
            string install = Path.Combine(root, "install");
            AppStoragePaths paths = AppStoragePaths.FromRoot(Path.Combine(root, "data-root"));
            Directory.CreateDirectory(release);
            Directory.CreateDirectory(install);
            Directory.CreateDirectory(paths.DataRoot);
            Directory.CreateDirectory(paths.RerankerModelsDirectory);

            string binaryPath = Path.Combine(release, "OnlyRag.App.dll");
            await File.WriteAllTextAsync(binaryPath, "new binary");
            await File.WriteAllTextAsync(Path.Combine(install, "OnlyRag.App.dll"), "old binary");
            string localConfig = Path.Combine(paths.DataRoot, "settings.json");
            string model = Path.Combine(paths.RerankerModelsDirectory, "model.onnx");
            await File.WriteAllTextAsync(localConfig, "keep");
            await File.WriteAllTextAsync(model, "keep model");

            string manifestPath = Path.Combine(root, "manifest.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(new
                {
                    version = "2.0.0",
                    files = new[]
                    {
                        new
                        {
                            path = "OnlyRag.App.dll",
                            sha256 = Hash(binaryPath),
                            sizeBytes = new FileInfo(binaryPath).Length
                        }
                    }
                }));

            SelectiveUpdateManager manager = new(paths, install);
            UpdateResult result = await manager.ApplyAsync(release, manifestPath);

            Assert.Equal("new binary", await File.ReadAllTextAsync(Path.Combine(install, "OnlyRag.App.dll")));
            Assert.Equal("keep", await File.ReadAllTextAsync(localConfig));
            Assert.Equal("keep model", await File.ReadAllTextAsync(model));
            Assert.Equal(["OnlyRag.App.dll"], result.UpdatedFiles);
            Assert.Empty(result.FailedFiles);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CheckModelIntegrityAsync_ReportsMissingOrCorruptFilesForOnDemandRepair()
    {
        string root = CreateRoot();
        try
        {
            AppStoragePaths paths = AppStoragePaths.FromRoot(root);
            Directory.CreateDirectory(paths.DataRoot);
            string modelPath = Path.Combine(paths.RerankerModelsDirectory, "model.onnx");
            Directory.CreateDirectory(paths.RerankerModelsDirectory);
            await File.WriteAllTextAsync(modelPath, "corrupt");
            await File.WriteAllTextAsync(
                Path.Combine(paths.DataRoot, "integrity-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    files = new[]
                    {
                        new
                        {
                            path = Path.GetRelativePath(paths.DataRoot, modelPath),
                            sha256 = Convert.ToHexString(SHA256.HashData("expected"u8.ToArray())),
                            sizeBytes = 8
                        }
                    }
                }));

            ModelIntegrityStatus status = await new SelectiveUpdateManager(paths, Path.Combine(root, "install"))
                .CheckModelIntegrityAsync();

            Assert.False(status.IsHealthy);
            Assert.True(status.RequiresOnDemandRepair);
            Assert.Equal("download", Assert.Single(status.Issues).DiagnosticAction);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Hash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
