namespace OnlyRag.Core;

public sealed record ImageModelUrlVerificationResponse(
    bool IsValid,
    string Message,
    string? RepositoryId,
    IReadOnlyList<string> FoundFiles,
    IReadOnlyList<string> MissingFiles,
    long TotalSizeBytes,
    string SuggestedDisplayName,
    IReadOnlyList<string> SuggestedRequiredFiles);
