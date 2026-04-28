namespace OnlyRag.Core;

public sealed record OfficeConversionSettings(
    string? LibreOfficePath,
    int ConversionTimeoutSeconds);
