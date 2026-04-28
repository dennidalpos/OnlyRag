namespace OnlyRag.Infrastructure.Ingestion;

public sealed record DocumentIngestionCheckpoint(
    int Version,
    long DocumentId,
    int NextBlock,
    int PageCount,
    int NextChunkOrdinal,
    string Mode);
