using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class AesBackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppStoragePaths _paths;
    private readonly AesBackupService _backupService;

    public AesBackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _paths = AppStoragePaths.FromRoot(_tempDir);
        _backupService = new AesBackupService(_paths);
        
        // Ensure data directory exists
        Directory.CreateDirectory(_paths.DataDirectory);
        File.WriteAllText(_paths.DatabasePath, "DUMMY SQLITE DATA");
    }

    [Fact]
    public async Task EncryptedBackup_CreateAndRestore_Succeeds()
    {
        string backupFile = Path.Combine(_tempDir, "test_backup.oragbak");
        string password = "SecretPassword123!";

        var createResult = await _backupService.CreateEncryptedBackupAsync(backupFile, password);
        Assert.True(createResult.Success);
        Assert.True(File.Exists(backupFile));
        Assert.True(createResult.FileSizeBytes > 0);

        // Modify local DB file to test restore overwrite
        File.WriteAllText(_paths.DatabasePath, "MODIFIED DATA");

        var restoreResult = await _backupService.RestoreFromEncryptedBackupAsync(backupFile, password);
        Assert.True(restoreResult.Success);

        string restoredContent = File.ReadAllText(_paths.DatabasePath);
        Assert.Equal("DUMMY SQLITE DATA", restoredContent);
    }

    [Fact]
    public async Task EncryptedBackup_WrongPassword_FailsAuthentication()
    {
        string backupFile = Path.Combine(_tempDir, "test_backup.oragbak");
        await _backupService.CreateEncryptedBackupAsync(backupFile, "CorrectPassword123!");

        var restoreResult = await _backupService.RestoreFromEncryptedBackupAsync(backupFile, "WrongPassword");
        Assert.False(restoreResult.Success);
        Assert.Contains("MAC", restoreResult.ErrorMessage);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore temp dir cleanup errors
        }
        GC.SuppressFinalize(this);
    }
}
