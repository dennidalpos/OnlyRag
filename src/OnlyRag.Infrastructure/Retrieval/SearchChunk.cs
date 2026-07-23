namespace OnlyRag.Infrastructure.Retrieval;

public sealed record SearchChunk(
    long ChunkId,
    long DocumentId,
    string DocumentName,
    int ChunkIndex,
    int? PageStart,
    int? PageEnd,
    string Content,
    long? ParentChunkId = null,
    string ChunkLevel = "Child",
    string? SectionHeading = null,
    string? ParentContent = null);
