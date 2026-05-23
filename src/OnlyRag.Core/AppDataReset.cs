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

        DeleteDirectoryContents(paths.DataRoot);
        return true;
    }

    private static void DeleteDirectoryContents(string dataRoot)
    {
        string fullRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(fullRoot))
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(fullRoot))
        {
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
