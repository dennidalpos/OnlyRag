namespace OnlyRag.Core;

public sealed record ImageModelLocalState(
    string ModelId,
    string State,
    bool IsDownloaded,
    bool IsVerified,
    long LocalSizeBytes,
    string LocalDirectory,
    string? VerificationError,
    long ExpectedSizeBytes = 0,
    long RemainingDownloadBytes = 0);
