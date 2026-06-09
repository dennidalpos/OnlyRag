namespace OnlyRag.Core;

public sealed record PdfExportSettings(
    string? LibreOfficePath,
    int ConversionTimeoutSeconds);
