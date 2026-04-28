using Microsoft.Data.Sqlite;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteRetrievalChunkRepository : IRetrievalChunkRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteRetrievalChunkRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyDictionary<long, SearchChunk>> GetChunksAsync(
        IReadOnlyCollection<long> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (chunkIds.Count == 0)
        {
            return new Dictionary<long, SearchChunk>();
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        string parameters = AddInParameters(command, "$chunk", chunkIds.Distinct().ToArray());
        command.CommandText =
            $$"""
            SELECT c.id, c.document_id, d.original_file_name, c.chunk_index,
                   c.page_start, c.page_end, c.content
            FROM chunks AS c
            INNER JOIN documents AS d ON d.id = c.document_id
            WHERE c.id IN ({{parameters}});
            """;

        Dictionary<long, SearchChunk> chunks = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            SearchChunk chunk = new(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetString(6));
            chunks[chunk.ChunkId] = chunk;
        }

        return chunks;
    }

    private static string AddInParameters(
        SqliteCommand command,
        string prefix,
        IReadOnlyList<long> values)
    {
        string[] names = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            string name = $"{prefix}{index}";
            names[index] = name;
            command.AddParameter(name, values[index]);
        }

        return string.Join(", ", names);
    }
}
