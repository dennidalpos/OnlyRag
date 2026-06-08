namespace OnlyRag.Core;

public sealed record ImageGenerationSettings(
    string Provider,
    string Automatic1111BaseUrl,
    string ComfyUiBaseUrl,
    int RequestTimeoutSeconds,
    bool TrustNonLocalEndpoint,
    string? Automatic1111Model,
    string? ComfyUiWorkflowJson);

