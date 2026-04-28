using Microsoft.Data.Sqlite;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteEmbeddingRepository : IEmbeddingRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteEmbeddingRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<DocumentChunkForEmbedding>> ListChunksNeedingEmbeddingAsync(
        long documentId,
        string model,
        int afterChunkIndex,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (take <= 0)
        {
            return [];
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.document_id, c.chunk_index, c.content, c.content_hash
            FROM chunks AS c
            LEFT JOIN embeddings AS e ON e.chunk_id = c.id AND e.model = $model
            WHERE c.document_id = $documentId
              AND c.chunk_index >= $afterChunkIndex
              AND LENGTH(TRIM(c.content)) > 0
              AND (e.id IS NULL OR e.content_hash <> c.content_hash)
            ORDER BY c.chunk_index ASC
            LIMIT $take;
            """;
        command.AddParameter("$documentId", documentId);
        command.AddParameter("$model", model);
        command.AddParameter("$afterChunkIndex", Math.Max(0, afterChunkIndex));
        command.AddParameter("$take", Math.Max(1, take));

        List<DocumentChunkForEmbedding> chunks = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            chunks.Add(new DocumentChunkForEmbedding(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        return chunks;
    }

    public async Task UpsertEmbeddingAsync(
        long chunkId,
        string model,
        string contentHash,
        IReadOnlyList<float> vector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (vector.Count == 0)
        {
            throw new ArgumentException("Vector must contain at least one dimension.", nameof(vector));
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        byte[] vectorBlob = SqliteVectorBlob.Serialize(vector);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO embeddings (
                chunk_id,
                model,
                dimensions,
                distance_metric,
                content_hash,
                vector_blob,
                created_at_utc
            )
            VALUES (
                $chunkId,
                $model,
                $dimensions,
                'cosine',
                $contentHash,
                $vectorBlob,
                $now
            )
            ON CONFLICT(chunk_id, model) DO UPDATE SET
                dimensions = excluded.dimensions,
                distance_metric = excluded.distance_metric,
                content_hash = excluded.content_hash,
                vector_blob = excluded.vector_blob,
                created_at_utc = excluded.created_at_utc;
            """;
        command.AddParameter("$chunkId", chunkId);
        command.AddParameter("$model", model);
        command.AddParameter("$dimensions", vector.Count);
        command.AddParameter("$contentHash", contentHash);
        command.AddParameter("$vectorBlob", vectorBlob);
        command.AddParameter("$now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentEmbeddingStatusSnapshot> GetDocumentEmbeddingStatusAsync(
        long documentId,
        string? model,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        int chunkCount;
        await using (SqliteCommand countChunks = connection.CreateCommand())
        {
            countChunks.CommandText = "SELECT COUNT(*) FROM chunks WHERE document_id = $documentId AND LENGTH(TRIM(content)) > 0;";
            countChunks.AddParameter("$documentId", documentId);
            chunkCount = Convert.ToInt32(await countChunks.ExecuteScalarAsync(cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(model) || chunkCount == 0)
        {
            return new DocumentEmbeddingStatusSnapshot(documentId, model, chunkCount, 0, null);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(e.id), MAX(e.created_at_utc)
            FROM chunks AS c
            LEFT JOIN embeddings AS e ON e.chunk_id = c.id
                AND e.model = $model
                AND e.content_hash = c.content_hash
            WHERE c.document_id = $documentId
              AND LENGTH(TRIM(c.content)) > 0;
            """;
        command.AddParameter("$documentId", documentId);
        command.AddParameter("$model", model);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DocumentEmbeddingStatusSnapshot(documentId, model, chunkCount, 0, null);
        }

        DateTimeOffset? lastEmbeddedAtUtc = reader.IsDBNull(1)
            ? null
            : DateTimeOffset.Parse(reader.GetString(1));

        return new DocumentEmbeddingStatusSnapshot(
            documentId,
            model,
            chunkCount,
            Convert.ToInt32(reader.GetInt64(0)),
            lastEmbeddedAtUtc);
    }

    public async Task<IReadOnlyList<StoredEmbeddingVector>> ListEmbeddingVectorsAsync(
        string model,
        long afterChunkId,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (take <= 0)
        {
            return [];
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT c.id, c.document_id, c.chunk_index, e.model, e.content_hash, e.vector_blob, e.dimensions
            FROM embeddings AS e
            INNER JOIN chunks AS c ON c.id = e.chunk_id
            WHERE e.model = $model
              AND e.chunk_id > $afterChunkId
              AND e.content_hash = c.content_hash
            ORDER BY e.chunk_id ASC
            LIMIT $take;
            """;
        command.AddParameter("$model", model);
        command.AddParameter("$afterChunkId", afterChunkId);
        command.AddParameter("$take", take);

        List<StoredEmbeddingVector> vectors = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            byte[] blob = (byte[])reader["vector_blob"];
            int dimensions = reader.GetInt32(6);
            vectors.Add(new StoredEmbeddingVector(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                SqliteVectorBlob.Deserialize(blob, dimensions)));
        }

        return vectors;
    }

    public async Task<int> CountTotalEmbeddingsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM embeddings;";
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)Math.Min(count, int.MaxValue) : 0;
    }

}
