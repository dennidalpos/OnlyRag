namespace OnlyRag.Infrastructure.Ingestion;

public sealed record DocumentIngestionOptions(
    int ChunkSizeTokens,
    int OverlapTokens)
{
    public const int DefaultChunkSizeTokens = 800;
    public const int DefaultOverlapTokens = 120;
    public const int MinimumChunkSizeTokens = 100;
    public const int MaximumChunkSizeTokens = 4000;
    public const int MaximumOverlapTokens = 1000;

    public static DocumentIngestionOptions Normalize(int chunkSizeTokens, int overlapTokens)
    {
        int normalizedChunkSize = Math.Clamp(
            chunkSizeTokens,
            MinimumChunkSizeTokens,
            MaximumChunkSizeTokens);
        int normalizedOverlap = Math.Clamp(overlapTokens, 0, Math.Min(MaximumOverlapTokens, normalizedChunkSize / 2));
        return new DocumentIngestionOptions(normalizedChunkSize, normalizedOverlap);
    }
}
