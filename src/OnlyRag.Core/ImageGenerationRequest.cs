namespace OnlyRag.Core;

public sealed record ImageGenerationRequest(
    string Provider,
    string Prompt,
    string? NegativePrompt,
    string? Model,
    int Width,
    int Height,
    int Steps,
    int BatchSize,
    long? Seed);

