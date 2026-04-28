namespace OnlyRag.Infrastructure.Ingestion;

public sealed record OfficeConverterAvailability(
    bool IsAvailable,
    string? ExecutablePath,
    string Message,
    string? Suggestion);

public sealed record OfficeConversionRequest(
    long DocumentId,
    string SourcePath,
    string OriginalFileName,
    string SourceExtension);

public sealed record OfficeConversionResult(
    string PdfPath,
    string TemporaryDirectory);

public sealed class OfficeConversionUnavailableException : InvalidOperationException
{
    public OfficeConversionUnavailableException(string message)
        : base(message)
    {
    }
}

public sealed class OfficeConversionException : InvalidOperationException
{
    public OfficeConversionException(string message)
        : base(message)
    {
    }

    public OfficeConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
