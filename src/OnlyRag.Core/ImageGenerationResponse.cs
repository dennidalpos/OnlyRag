namespace OnlyRag.Core;

public sealed record ImageGenerationResponse(
    string Provider,
    string Message,
    IReadOnlyList<GeneratedImage> Images);

