namespace OnlyRag.Core;

public sealed record DocumentSearchResponse(
    IReadOnlyList<DocumentSearchResult> Results,
    IReadOnlyList<DocumentSearchDocumentStatus> Documents,
    string KeywordBackend,
    string VectorBackend,
    int MaxContextCharacters);

public sealed record DocumentSearchResult(
    long DocumentId,
    string DocumentName,
    int? PageStart,
    int? PageEnd,
    long ChunkId,
    string Snippet,
    double Score);

public sealed record DocumentSearchDocumentStatus(
    long DocumentId,
    string DocumentName,
    DocumentStatus Status,
    bool IsIndexed,
    string EmbeddingState,
    int ChunkCount,
    int EmbeddedChunkCount);
