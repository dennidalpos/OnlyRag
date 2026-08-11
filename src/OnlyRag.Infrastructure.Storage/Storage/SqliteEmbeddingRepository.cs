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
            LEFT JOIN chunk_vector_index_status AS s ON s.chunk_id = c.id AND s.model = $model
            WHERE c.document_id = $documentId
              AND c.chunk_index >= $afterChunkIndex
              AND LENGTH(TRIM(c.content)) > 0
              AND (s.id IS NULL OR s.status <> 'Indexed' OR s.content_hash <> c.content_hash)
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

    public Task MarkChunkIndexedAsync(
        long chunkId,
        string model,
        string contentHash,
        int dimensions,
        string qdrantCollection,
        string qdrantPointId,
        CancellationToken cancellationToken = default)
    {
        return UpsertStatusAsync(
            chunkId,
            model,
            contentHash,
            dimensions,
            qdrantCollection,
            qdrantPointId,
            "Indexed",
            lastError: null,
            indexedAtUtc: DateTimeOffset.UtcNow,
            cancellationToken);
    }

    public Task MarkChunkIndexFailedAsync(
        long chunkId,
        string model,
        string contentHash,
        int dimensions,
        string qdrantCollection,
        string qdrantPointId,
        string lastError,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lastError);
        return UpsertStatusAsync(
            chunkId,
            model,
            contentHash,
            dimensions,
            qdrantCollection,
            qdrantPointId,
            "Failed",
            lastError,
            indexedAtUtc: null,
            cancellationToken);
    }

    private async Task UpsertStatusAsync(
        long chunkId,
        string model,
        string contentHash,
        int dimensions,
        string qdrantCollection,
        string qdrantPointId,
        string status,
        string? lastError,
        DateTimeOffset? indexedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(qdrantCollection);
        ArgumentException.ThrowIfNullOrWhiteSpace(qdrantPointId);
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be positive.");
        }

        string now = DateTimeOffset.UtcNow.ToString("O");
        string? indexedAt = indexedAtUtc?.ToString("O");

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO chunk_vector_index_status (
                chunk_id,
                model,
                dimensions,
                content_hash,
                qdrant_collection,
                qdrant_point_id,
                indexed_at_utc,
                status,
                last_error,
                updated_at_utc
            )
            VALUES (
                $chunkId,
                $model,
                $dimensions,
                $contentHash,
                $qdrantCollection,
                $qdrantPointId,
                $indexedAtUtc,
                $status,
                $lastError,
                $now
            )
            ON CONFLICT(chunk_id, model) DO UPDATE SET
                dimensions = excluded.dimensions,
                content_hash = excluded.content_hash,
                qdrant_collection = excluded.qdrant_collection,
                qdrant_point_id = excluded.qdrant_point_id,
                indexed_at_utc = excluded.indexed_at_utc,
                status = excluded.status,
                last_error = excluded.last_error,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.AddParameter("$chunkId", chunkId);
        command.AddParameter("$model", model);
        command.AddParameter("$dimensions", dimensions);
        command.AddParameter("$contentHash", contentHash);
        command.AddParameter("$qdrantCollection", qdrantCollection);
        command.AddParameter("$qdrantPointId", qdrantPointId);
        command.AddParameter("$indexedAtUtc", indexedAt);
        command.AddParameter("$status", status);
        command.AddParameter("$lastError", lastError);
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
            SELECT COUNT(s.id), MAX(s.indexed_at_utc)
            FROM chunks AS c
            LEFT JOIN chunk_vector_index_status AS s ON s.chunk_id = c.id
                AND s.model = $model
                AND s.content_hash = c.content_hash
                AND s.status = 'Indexed'
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

    public async Task<IReadOnlyList<DocumentVectorIndexReference>> ListDocumentVectorIndexReferencesAsync(
        long documentId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT s.model, s.dimensions
            FROM chunk_vector_index_status AS s
            INNER JOIN chunks AS c ON c.id = s.chunk_id
            WHERE c.document_id = $documentId
              AND s.dimensions > 0
              AND LENGTH(TRIM(s.model)) > 0
            ORDER BY s.model, s.dimensions;
            """;
        command.AddParameter("$documentId", documentId);

        List<DocumentVectorIndexReference> references = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            references.Add(new DocumentVectorIndexReference(
                reader.GetString(0),
                reader.GetInt32(1)));
        }

        return references;
    }

    public async Task<int> CountIndexedChunksAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM chunk_vector_index_status WHERE status = 'Indexed';";
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)Math.Min(count, int.MaxValue) : 0;
    }

}
