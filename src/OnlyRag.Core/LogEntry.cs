namespace OnlyRag.Core;

public sealed record LogEntry(
    string Id,
    DateTime TimestampUtc,
    AppLogLevel Level,
    string Category,
    string Message,
    string? ExceptionDetails = null,
    string? DataJson = null);
