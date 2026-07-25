namespace OnlyRag.Core;

public sealed record LogStorageInfo(
    long TotalSizeBytes,
    string FormattedSize,
    int MemoryEntryCount,
    int FileCount,
    string LogDirectory);
