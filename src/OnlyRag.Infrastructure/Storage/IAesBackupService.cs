namespace OnlyRag.Infrastructure.Storage;

public record BackupResult(bool Success, string BackupPath, long FileSizeBytes, string? ErrorMessage = null);

public record RestoreResult(bool Success, string? ErrorMessage = null);

public interface IAesBackupService
{
    Task<BackupResult> CreateEncryptedBackupAsync(string destinationFilePath, string password, CancellationToken cancellationToken = default);

    Task<RestoreResult> RestoreFromEncryptedBackupAsync(string sourceFilePath, string password, CancellationToken cancellationToken = default);
}
