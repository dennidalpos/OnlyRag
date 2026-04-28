namespace OnlyRag.Infrastructure.Ingestion;

public sealed record DocumentIngestionResult(
    int PageCount,
    int ChunkCount);
