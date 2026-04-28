namespace OnlyRag.Core;

public sealed record ChatResponse(
    string ConversationId,
    string Model,
    string Answer,
    bool UsedDocuments,
    IReadOnlyList<ChatSource> Sources,
    IReadOnlyList<ChatNotice> Notices);

public sealed record ChatSource(
    long DocumentId,
    string DocumentName,
    int? PageStart,
    int? PageEnd,
    long ChunkId,
    string Snippet,
    double Score);

public sealed record ChatNotice(
    string Code,
    string Message);

