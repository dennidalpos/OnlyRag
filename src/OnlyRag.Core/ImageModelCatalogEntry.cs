namespace OnlyRag.Core;

public sealed record ImageModelCatalogEntry(
    string Id,
    string DisplayName,
    string RecommendedProfile,
    string DownloadUrl,
    string LicenseLabel,
    long ExpectedSizeBytes,
    IReadOnlyList<string> RequiredFiles,
    string Sha256,
    bool IsBuiltIn,
    string ModelType = "SDXL ONNX",
    string ModelProfile = "custom",
    IReadOnlyList<string> SupportedResolutions = null!,
    int DefaultSteps = 6,
    double DefaultGuidance = 0,
    string Scheduler = "Runtime default",
    string CompatibilityNotes = "DirectML GPU preferred; CPU fallback supported.");
