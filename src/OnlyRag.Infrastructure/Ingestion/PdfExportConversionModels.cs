namespace OnlyRag.Infrastructure.Ingestion;

public sealed record PdfExportConverterAvailability(
    bool IsAvailable,
    string? ExecutablePath,
    string Message,
    string? Suggestion);

public sealed record PdfExportConversionRequest(
    long DocumentId,
    string SourcePath,
    string OriginalFileName,
    string SourceExtension);

public sealed record PdfExportConversionResult(
    string PdfPath,
    string TemporaryDirectory);

public sealed class PdfExportConversionUnavailableException : InvalidOperationException
{
    public PdfExportConversionUnavailableException(string message)
        : base(message)
    {
    }
}

public sealed class PdfExportConversionException : InvalidOperationException
{
    public PdfExportConversionException(string message)
        : base(message)
    {
    }

    public PdfExportConversionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
