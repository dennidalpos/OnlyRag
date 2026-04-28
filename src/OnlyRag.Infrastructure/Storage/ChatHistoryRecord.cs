namespace OnlyRag.Infrastructure.Storage;

public sealed record ChatHistoryRecord(
    long Id,
    string ConversationId,
    string Role,
    string Content,
    string? Model,
    string? MetadataJson,
    DateTimeOffset CreatedAtUtc);

