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
        string parameters = command.AddInParameters("$chunk", chunkIds.Distinct().ToArray());
        command.CommandText =
            $$"""
            SELECT c.id, c.document_id, d.original_file_name, c.chunk_index,
                   c.page_start, c.page_end, c.content, c.parent_chunk_id,
                   c.chunk_level, c.section_heading, pc.content AS parent_content
            FROM chunks AS c
            INNER JOIN documents AS d ON d.id = c.document_id
            LEFT JOIN chunks AS pc ON pc.id = c.parent_chunk_id
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
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? "Child" : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10));
            chunks[chunk.ChunkId] = chunk;
        }

        return chunks;
    }
}
