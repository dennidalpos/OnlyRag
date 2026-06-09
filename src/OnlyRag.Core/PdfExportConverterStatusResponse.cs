namespace OnlyRag.Core;

public sealed record PdfExportConverterStatusResponse(
    string State,
    bool IsAvailable,
    string? ExecutablePath,
    string Message,
    string? Suggestion,
    int ConversionTimeoutSeconds);
