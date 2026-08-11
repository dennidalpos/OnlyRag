namespace OnlyRag.Infrastructure.Storage;

public enum DocumentStorageLimitKind
{
    TooManyFiles,
    FileTooLarge,
    BatchTooLarge,
    LibraryQuotaExceeded,
    LowDiskSpace
}

public sealed class DocumentStorageLimitException : InvalidOperationException
{
    public DocumentStorageLimitException(
        DocumentStorageLimitKind kind,
        string title,
        string message)
        : base(message)
    {
        Kind = kind;
        Title = title;
    }

    public DocumentStorageLimitKind Kind { get; }

    public string Title { get; }
}
