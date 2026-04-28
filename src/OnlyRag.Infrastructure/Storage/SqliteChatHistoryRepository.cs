using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteChatHistoryRepository : IChatHistoryRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteChatHistoryRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ChatHistoryRecord>> ListRecentMessagesAsync(
        string conversationId,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || maxMessages <= 0)
        {
            return [];
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, conversation_id, role, content, model, metadata_json, created_at_utc
            FROM (
                SELECT id, conversation_id, role, content, model, metadata_json, created_at_utc
                FROM chat_messages
                WHERE conversation_id = $conversationId
                ORDER BY id DESC
                LIMIT $limit
            )
            ORDER BY id ASC;
            """;
        command.AddParameter("$conversationId", conversationId);
        command.AddParameter("$limit", maxMessages);

        List<ChatHistoryRecord> messages = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatHistoryRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6))));
        }

        return messages;
    }

    public async Task AppendMessageAsync(
        string conversationId,
        string role,
        string content,
        string? model,
        string? metadataJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (SqliteCommand upsertConversation = connection.CreateCommand())
        {
            upsertConversation.Transaction = transaction;
            upsertConversation.CommandText =
                """
                INSERT INTO chat_conversations(conversation_id, title, created_at_utc, updated_at_utc)
                VALUES ($conversationId, $title, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'), strftime('%Y-%m-%dT%H:%M:%fZ', 'now'))
                ON CONFLICT(conversation_id) DO UPDATE SET
                    updated_at_utc = excluded.updated_at_utc;
                """;
            upsertConversation.AddParameter("$conversationId", conversationId);
            upsertConversation.AddParameter("$title", BuildTitle(content));
            await upsertConversation.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand insertMessage = connection.CreateCommand())
        {
            insertMessage.Transaction = transaction;
            insertMessage.CommandText =
                """
                INSERT INTO chat_messages(conversation_id, role, content, model, metadata_json, created_at_utc)
                VALUES ($conversationId, $role, $content, $model, $metadataJson, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
                """;
            insertMessage.AddParameter("$conversationId", conversationId);
            insertMessage.AddParameter("$role", role);
            insertMessage.AddParameter("$content", content);
            insertMessage.AddParameter("$model", model);
            insertMessage.AddParameter("$metadataJson", metadataJson);
            await insertMessage.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static string BuildTitle(string content)
    {
        string normalized = string.Join(" ", content.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}

