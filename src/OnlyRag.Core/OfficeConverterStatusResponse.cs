namespace OnlyRag.Core;

public sealed record OfficeConverterStatusResponse(
    string State,
    bool IsAvailable,
    string? ExecutablePath,
    string Message,
    string? Suggestion,
    int ConversionTimeoutSeconds);
