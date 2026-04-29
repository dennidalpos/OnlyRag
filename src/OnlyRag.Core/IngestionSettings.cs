namespace OnlyRag.Core;

public sealed record IngestionSettings(
    int ChunkSizeTokens,
    int OverlapTokens);
