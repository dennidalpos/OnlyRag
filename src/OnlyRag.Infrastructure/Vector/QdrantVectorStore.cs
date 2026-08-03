using System.Security.Cryptography;
using System.Text;
using OnlyRag.Core;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace OnlyRag.Infrastructure.Vector;

public sealed class QdrantVectorStore : IQdrantVectorStore, IAsyncDisposable
{
    private readonly QdrantSettingsStore settingsStore;
    private QdrantClient? _cachedClient;
    private QdrantSettings? _cachedSettings;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    public QdrantVectorStore(QdrantSettingsStore settingsStore)
    {
        this.settingsStore = settingsStore;
    }

    public string BackendName => "Qdrant gRPC (cosine, persistent external vector store)";

    public int MaxSearchableVectors => int.MaxValue;

    public bool IsVectorStoragePersistent => true;

    public string BuildCollectionName(string model, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be positive.");
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(model.Trim().ToLowerInvariant()));
        string suffix = Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
        return $"onlyrag_{dimensions}_{suffix}";
    }

    public string BuildPointId(long chunkId)
    {
        if (chunkId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkId), "Chunk id must be positive.");
        }

        return chunkId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task VerifyAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        QdrantClient client = await GetOrCreateClientAsync(cancellationToken);
        await client.HealthAsync(cancellationToken);
    }

    public async Task UpsertChunkAsync(
        long chunkId,
        long documentId,
        int chunkIndex,
        string model,
        string contentHash,
        IReadOnlyList<float> vector,
        CancellationToken cancellationToken = default)
    {
        await UpsertChunkBatchAsync(
            [new QdrantChunkPayload(chunkId, documentId, chunkIndex, model, contentHash, vector)],
            cancellationToken);
    }

    public async Task UpsertChunkBatchAsync(
        IReadOnlyList<QdrantChunkPayload> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks == null || chunks.Count == 0) return;

        var groups = chunks
            .Where(c => c.Vector.Count > 0 && !string.IsNullOrWhiteSpace(c.Model) && !string.IsNullOrWhiteSpace(c.ContentHash))
            .GroupBy(c => BuildCollectionName(c.Model, c.Vector.Count));

        QdrantClient client = await GetOrCreateClientAsync(cancellationToken);

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string collection = group.Key;
            int dimensions = group.First().Vector.Count;

            QdrantQuantizationMode mode = _cachedSettings?.QuantizationMode ?? QdrantQuantizationMode.ScalarSQ8;
            await EnsureCollectionAsync(client, collection, dimensions, mode, cancellationToken);

            var uniqueChunks = group
                .GroupBy(c => c.ContentHash)
                .Select(g => g.First())
                .ToList();

            var points = uniqueChunks.Select(c =>
            {
                ulong pointId = checked((ulong)c.ChunkId);
                PointStruct point = new()
                {
                    Id = new PointId { Num = pointId },
                    Vectors = c.Vector.ToArray()
                };
                point.Payload["chunk_id"] = c.ChunkId;
                point.Payload["document_id"] = c.DocumentId;
                point.Payload["chunk_index"] = c.ChunkIndex;
                point.Payload["model"] = c.Model;
                point.Payload["content_hash"] = c.ContentHash;
                return point;
            }).ToList();

            if (points.Count > 0)
            {
                await client.UpsertAsync(collection, points, cancellationToken: cancellationToken);
            }
        }
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string model,
        IReadOnlyList<float> queryVector,
        IReadOnlyCollection<long> documentIds,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (queryVector.Count == 0 || documentIds.Count == 0 || limit <= 0)
        {
            return [];
        }

        string collection = BuildCollectionName(model, queryVector.Count);
        QdrantClient client = await GetOrCreateClientAsync(cancellationToken);
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            throw new InvalidOperationException($"Collection Qdrant mancante: {collection}. Ricostruire gli embedding.");
        }

        long[] filteredDocumentIds = documentIds.Where(id => id > 0).Distinct().ToArray();
        Filter filter = new() { Must = { Match("document_id", filteredDocumentIds) } };
        IReadOnlyList<ScoredPoint> points = await client.SearchAsync(
            collection,
            queryVector.ToArray(),
            filter: filter,
            limit: (ulong)Math.Max(1, limit),
            payloadSelector: new WithPayloadSelector { Enable = true },
            vectorsSelector: new WithVectorsSelector { Enable = false },
            cancellationToken: cancellationToken);

        return points
            .Select(point => new VectorSearchResult(
                ToInt64(point.Id),
                ReadLongPayload(point, "document_id"),
                (int)ReadLongPayload(point, "chunk_index"),
                point.Score))
            .ToArray();
    }

    public async Task DeleteDocumentAsync(
        string model,
        int dimensions,
        long documentId,
        CancellationToken cancellationToken = default)
    {
        string collection = BuildCollectionName(model, dimensions);
        QdrantClient client = await GetOrCreateClientAsync(cancellationToken);
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        Filter filter = new() { Must = { Match("document_id", documentId) } };
        await client.DeleteAsync(collection, filter, cancellationToken: cancellationToken);
    }

    public async Task OptimizeCollectionAsync(
        string model,
        int dimensions,
        CancellationToken cancellationToken = default)
    {
        string collection = BuildCollectionName(model, dimensions);
        QdrantClient client = await GetOrCreateClientAsync(cancellationToken);
        if (!await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        CollectionInfo info = await client.GetCollectionInfoAsync(collection, cancellationToken);
        ulong count = info.PointsCount;
        HnswConfigDiff hnswConfig = QdrantHnswTuner.BuildHnswConfigDiff(count);

        await client.UpdateCollectionAsync(
            collection,
            hnswConfig: hnswConfig,
            cancellationToken: cancellationToken);
    }

    private async Task<QdrantClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedClient != null && _cachedSettings != null &&
                _cachedSettings.GrpcEndpoint == settings.GrpcEndpoint &&
                _cachedSettings.ApiKey == settings.ApiKey &&
                _cachedSettings.RequestTimeoutSeconds == settings.RequestTimeoutSeconds)
            {
                return _cachedClient;
            }

            if (_cachedClient != null)
            {
                _cachedClient.Dispose();
            }

            Uri endpoint = QdrantSettingsStore.ParseEndpoint(settings.GrpcEndpoint);
            _cachedClient = new QdrantClient(endpoint, settings.ApiKey, TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
            _cachedSettings = settings;

            return _cachedClient;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _clientLock.WaitAsync();
        try
        {
            if (_cachedClient != null)
            {
                _cachedClient.Dispose();
                _cachedClient = null;
            }
        }
        finally
        {
            _clientLock.Release();
            _clientLock.Dispose();
        }
    }

    private static async Task EnsureCollectionAsync(
        QdrantClient client,
        string collection,
        int dimensions,
        QdrantQuantizationMode quantizationMode,
        CancellationToken cancellationToken)
    {
        if (await client.CollectionExistsAsync(collection, cancellationToken))
        {
            return;
        }

        QuantizationConfig? quantizationConfig = quantizationMode switch
        {
            QdrantQuantizationMode.ScalarSQ8 => new QuantizationConfig
            {
                Scalar = new ScalarQuantization
                {
                    Type = QuantizationType.Int8,
                    AlwaysRam = true
                }
            },
            QdrantQuantizationMode.ProductPQ => new QuantizationConfig
            {
                Product = new ProductQuantization
                {
                    Compression = CompressionRatio.X8,
                    AlwaysRam = true
                }
            },
            _ => null
        };

        await client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine },
            hnswConfig: QdrantHnswTuner.BuildHnswConfigDiff(0),
            quantizationConfig: quantizationConfig,
            cancellationToken: cancellationToken);
    }

    private static long ToInt64(PointId pointId)
    {
        if (pointId.HasNum)
        {
            return checked((long)pointId.Num);
        }

        throw new InvalidOperationException("Qdrant ha restituito un point id non numerico per un chunk OnlyRag.");
    }

    private static long ReadLongPayload(ScoredPoint point, string key)
    {
        if (!point.Payload.TryGetValue(key, out Value? value) || !value.HasIntegerValue)
        {
            throw new InvalidOperationException($"Payload Qdrant mancante o non numerico: {key}.");
        }

        return value.IntegerValue;
    }
}
