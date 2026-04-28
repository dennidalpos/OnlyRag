namespace OnlyRag.Infrastructure.Storage;

public interface IChatHistoryRepository
{
    Task<IReadOnlyList<ChatHistoryRecord>> ListRecentMessagesAsync(
        string conversationId,
        int maxMessages,
        CancellationToken cancellationToken = default);

    Task AppendMessageAsync(
        string conversationId,
        string role,
        string content,
        string? model,
        string? metadataJson,
        CancellationToken cancellationToken = default);
}

