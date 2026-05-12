namespace OnlyRag.Infrastructure.Storage;

public sealed record LocalDocumentLibraryLimits
{
    public const long MiB = 1024L * 1024L;
    public const long GiB = 1024L * MiB;

    public static LocalDocumentLibraryLimits Default { get; } = new();

    public int MaxFilesPerImport { get; init; } = 50;

    public long MaxFileSizeBytes { get; init; } = 100 * MiB;

    public long MaxBatchSizeBytes { get; init; } = 500 * MiB;

    public long LibraryQuotaBytes { get; init; } = 10 * GiB;

    public long MinimumFreeDiskBytes { get; init; } = 1 * GiB;

    public long MaxRequestBodySizeBytes => MaxBatchSizeBytes + MiB;
}
