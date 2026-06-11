namespace OnlyRag.Core;

public sealed record ImageModelDownloadResponse(
    string ModelId,
    string State,
    string Message);
