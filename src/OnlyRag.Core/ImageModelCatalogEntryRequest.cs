namespace OnlyRag.Core;

public sealed record ImageModelCatalogEntryRequest(
    string Id,
    string DisplayName,
    string RecommendedProfile,
    string DownloadUrl,
    string LicenseLabel,
    long ExpectedSizeBytes,
    IReadOnlyList<string> RequiredFiles,
    string Sha256);
