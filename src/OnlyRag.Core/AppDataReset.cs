namespace OnlyRag.Core;

public static class AppDataReset
{
    public static void RequestResetOnNextStartup(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Directory.CreateDirectory(paths.DataRoot);
        File.WriteAllText(
            paths.PendingResetMarkerPath,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool ApplyPendingReset(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!File.Exists(paths.PendingResetMarkerPath))
        {
            return false;
        }

        CreateTimestampedBackup(paths);
        DeleteDirectoryContents(paths.DataRoot);
        return true;
    }

    public static void ResetNow(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        CreateTimestampedBackup(paths);
        DeleteDirectoryContents(paths.DataRoot);
    }

    public static AppDataResetBackup CreateTimestampedBackup(AppStoragePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        Directory.CreateDirectory(paths.BackupsDirectory);
        string backupPath = CreateUniqueBackupPath(paths.BackupsDirectory);
        Directory.CreateDirectory(backupPath);
        CopyDirectoryContents(paths.DataRoot, backupPath, paths.BackupsDirectory);
        return new AppDataResetBackup(backupPath);
    }

    private static void DeleteDirectoryContents(string dataRoot)
    {
        string fullRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(fullRoot))
        {
            return;
        }

        string backupRoot = Path.GetFullPath(AppStoragePaths.FromRoot(fullRoot).BackupsDirectory);
        foreach (string entry in Directory.EnumerateFileSystemEntries(fullRoot))
        {
            if (IsSamePath(entry, backupRoot))
            {
                continue;
            }

            if (Directory.Exists(entry))
            {
                ClearAttributesRecursive(entry);
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
    }

    private static void CopyDirectoryContents(string sourceRoot, string destinationRoot, string backupsDirectory)
    {
        string fullSourceRoot = Path.GetFullPath(sourceRoot);
        string fullDestinationRoot = Path.GetFullPath(destinationRoot);
        string fullBackupsDirectory = Path.GetFullPath(backupsDirectory);
        foreach (string sourcePath in Directory.EnumerateFileSystemEntries(fullSourceRoot))
        {
            if (IsSamePath(sourcePath, fullBackupsDirectory))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(fullSourceRoot, sourcePath);
            string destinationPath = Path.Combine(fullDestinationRoot, relativePath);
            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destinationPath);
                CopyDirectoryContents(sourcePath, destinationPath, fullBackupsDirectory);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: false);
            }
        }
    }

    private static string CreateUniqueBackupPath(string backupsDirectory)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string backupPath = Path.Combine(backupsDirectory, $"reset-{timestamp}");
        if (!Directory.Exists(backupPath))
        {
            return backupPath;
        }

        for (int suffix = 2; suffix < 100; suffix++)
        {
            string candidate = Path.Combine(backupsDirectory, $"reset-{timestamp}-{suffix}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(backupsDirectory, $"reset-{timestamp}-{Guid.NewGuid():N}");
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearAttributesRecursive(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        foreach (string childDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(childDirectory, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }
}

public sealed record AppDataResetBackup(string BackupPath);
