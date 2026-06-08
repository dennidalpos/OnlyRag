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

