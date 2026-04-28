namespace OnlyRag.Infrastructure.Storage;

public sealed record IngestedDocumentChunk(
    int PageStart,
    int PageEnd,
    int Ordinal,
    string Text,
    int ApproximateTokenCount,
    string ContentHash);
