namespace OnlyRag.Core;

public sealed record VectorSearchResult(
    long ChunkId,
    long DocumentId,
    int ChunkIndex,
    double Score);

public enum QdrantQuantizationMode
{
    Disabled = 0,
    ScalarSQ8 = 1,
    ProductPQ = 2
}

public sealed record QdrantSettings(
    string GrpcEndpoint = "http://127.0.0.1:6334",
    string? ApiKey = null,
    bool TrustNonLoopbackEndpoint = false,
    bool RequireTlsForRemoteEndpoint = true,
    bool UseLocalBundledServer = true,
    int LocalGrpcPort = 6334,
    int RequestTimeoutSeconds = 30,
    QdrantQuantizationMode QuantizationMode = QdrantQuantizationMode.ScalarSQ8);

public sealed record QdrantSettingsResponse(
    string GrpcEndpoint,
    bool HasApiKey,
    bool TrustNonLoopbackEndpoint,
    bool RequireTlsForRemoteEndpoint,
    bool UseLocalBundledServer,
    int LocalGrpcPort,
    int RequestTimeoutSeconds,
    QdrantQuantizationMode QuantizationMode);

public sealed record QdrantStatusResponse(
    string Status,
    bool IsReachable,
    string GrpcEndpoint,
    bool IsLoopback,
    bool IsTls,
    bool HasApiKey,
    string? Version,
    string? BinaryPath,
    string? ConfigPath,
    string? StorageDirectory,
    int? ProcessId,
    string? Warning,
    string? Error,
    bool AutoHealingActive = false,
    int AutoHealRestartCount = 0,
    DateTimeOffset? LastAutoHealedAtUtc = null);

public enum QueryTransformationStrategy
{
    None = 0,
    MultiQuery = 1,
    SubQuery = 2,
    HyDE = 3
}

public sealed record RetrievalSettings(
    bool EnableReRanker = true,
    double ReRankerCutoffThreshold = 0.35,
    int TopCandidatesCount = 40,
    int FinalTopK = 5,
    QueryTransformationStrategy TransformationStrategy = QueryTransformationStrategy.MultiQuery,
    int ChildChunkTokens = 150,
    int ParentChunkTokens = 1000,
    double CragConfidenceThreshold = 0.30)
{
    public static RetrievalSettings Default { get; } = new();
}

public sealed record RagLatencyMetrics(
    double QueryEmbeddingMs,
    double QdrantSearchMs,
    double Fts5SearchMs,
    double ReRankingMs,
    double TotalMs,
    double AverageCragScore = 0.0,
    double P99Ms = 0.0);

