namespace OnlyRag.Infrastructure.Storage;

public sealed class LocalDocumentStorageGuard
{
    private readonly LocalSqliteStoreDescriptor descriptor;

    public LocalDocumentStorageGuard(
        LocalSqliteStoreDescriptor descriptor,
        LocalDocumentLibraryLimits limits)
    {
        this.descriptor = descriptor;
        Limits = limits;
    }

    public LocalDocumentLibraryLimits Limits { get; }

    public void EnsureFileWithinLimits(string fileName, long fileSizeBytes)
    {
        if (fileSizeBytes > Limits.MaxFileSizeBytes)
        {
            throw new DocumentStorageLimitException(
                DocumentStorageLimitKind.FileTooLarge,
                "File troppo grande",
                $"Il file '{fileName}' supera il limite di {LocalDocumentLibraryLimits.FormatBytes(Limits.MaxFileSizeBytes)}.");
        }
    }

    public void EnsureBatchWithinLimits(int fileCount, long batchSizeBytes)
    {
        if (fileCount > Limits.MaxFilesPerImport)
        {
            throw new DocumentStorageLimitException(
                DocumentStorageLimitKind.TooManyFiles,
                "Troppi file",
                $"Importa al massimo {Limits.MaxFilesPerImport} file per operazione.");
        }

        if (batchSizeBytes > Limits.MaxBatchSizeBytes)
        {
            throw new DocumentStorageLimitException(
                DocumentStorageLimitKind.BatchTooLarge,
                "Import troppo grande",
                $"La dimensione totale dell'import supera il limite di {LocalDocumentLibraryLimits.FormatBytes(Limits.MaxBatchSizeBytes)}.");
        }
    }

    public void EnsureStorageAvailableForBytes(long incomingBytes)
    {
        long currentUsage = GetCurrentLibraryUsageBytes();
        if (currentUsage + incomingBytes > Limits.LibraryQuotaBytes)
        {
            throw new DocumentStorageLimitException(
                DocumentStorageLimitKind.LibraryQuotaExceeded,
                "Quota libreria superata",
                $"La libreria locale supererebbe la quota di {LocalDocumentLibraryLimits.FormatBytes(Limits.LibraryQuotaBytes)}.");
        }

        string root = descriptor.Paths.DataRoot;
        Directory.CreateDirectory(root);
        DriveInfo drive = new(Path.GetPathRoot(Path.GetFullPath(root)) ?? root);
        if (drive.AvailableFreeSpace - incomingBytes < Limits.MinimumFreeDiskBytes)
        {
            throw new DocumentStorageLimitException(
                DocumentStorageLimitKind.LowDiskSpace,
                "Spazio disco insufficiente",
                $"Mantieni almeno {LocalDocumentLibraryLimits.FormatBytes(Limits.MinimumFreeDiskBytes)} liberi sul disco della libreria.");
        }
    }

    private long GetCurrentLibraryUsageBytes()
    {
        string originals = descriptor.Paths.DocumentOriginalsDirectory;
        if (!Directory.Exists(originals))
        {
            return 0;
        }

        return Directory.EnumerateFiles(originals, "*", SearchOption.AllDirectories)
            .Sum(path =>
            {
                try
                {
                    return new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    return 0;
                }
                catch (UnauthorizedAccessException)
                {
                    return 0;
                }
            });
    }
}
