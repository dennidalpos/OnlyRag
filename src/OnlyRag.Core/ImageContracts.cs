namespace OnlyRag.Core;

public sealed record GeneratedImage(
    long Id,
    string Provider,
    string Prompt,
    string? NegativePrompt,
    string? Model,
    int Width,
    int Height,
    int Steps,
    int BatchSize,
    long? Seed,
    string FileName,
    string MimeType,
    long FileSizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record ImageGenerationRequest(
    string Prompt,
    string? NegativePrompt,
    string? ModelId,
    int Width,
    int Height,
    int Steps,
    int BatchSize,
    long? Seed,
    float? GuidanceScale = null);

public sealed record ImageGenerationResponse(
    string Provider,
    string Message,
    IReadOnlyList<GeneratedImage> Images);

public sealed record ImageGenerationRuntimeStatus(
    string State,
    bool IsReady,
    string ExecutionProvider,
    string Message,
    string? Suggestion,
    string PreferredExecutionProvider = "CPU",
    string ModelState = "Unknown",
    string? FallbackReason = null);

public sealed record ImageGenerationSettings(
    string SelectedModelId,
    int RequestTimeoutSeconds,
    bool PreferGpu);

public sealed record ImagePromptTranslationRequest(
    string Prompt,
    string? SourceLanguage = null);

public sealed record ImagePromptTranslationResponse(
    string OriginalPrompt,
    string TranslatedPrompt,
    string TargetLanguage,
    bool WasTranslated = false);

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

public sealed record ImageModelCatalogEntryRequest(
    string Id,
    string DisplayName,
    string RecommendedProfile,
    string DownloadUrl,
    string LicenseLabel,
    long ExpectedSizeBytes,
    IReadOnlyList<string> RequiredFiles,
    string Sha256,
    string ModelType = "SDXL ONNX",
    string ModelProfile = "custom",
    IReadOnlyList<string> SupportedResolutions = null!,
    int DefaultSteps = 6,
    double DefaultGuidance = 0,
    string Scheduler = "Runtime default",
    string CompatibilityNotes = "DirectML GPU preferred; CPU fallback supported.");

public sealed record ImageModelDownloadRequest(bool ConsentConfirmed);

public sealed record ImageModelDownloadResponse(
    string ModelId,
    string State,
    string Message);

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

public sealed record ImageModelUrlVerificationRequest(string Url);

public sealed record ImageModelUrlVerificationResponse(
    bool IsValid,
    string Message,
    string? RepositoryId,
    IReadOnlyList<string> FoundFiles,
    IReadOnlyList<string> MissingFiles,
    long TotalSizeBytes,
    string SuggestedDisplayName,
    IReadOnlyList<string> SuggestedRequiredFiles);

public sealed record ImageEditSaveRequest(
    string ImageBase64,
    string MimeType,
    int Width,
    int Height,
    bool ReplaceOriginal);
