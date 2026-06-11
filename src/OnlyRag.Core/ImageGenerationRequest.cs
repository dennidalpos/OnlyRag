namespace OnlyRag.Core;

public sealed record ImageGenerationRequest(
    string Prompt,
    string? NegativePrompt,
    string? ModelId,
    int Width,
    int Height,
    int Steps,
    int BatchSize,
    long? Seed);
