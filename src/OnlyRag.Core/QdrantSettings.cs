namespace OnlyRag.Core;

public sealed record QdrantSettings(
    string GrpcEndpoint = "http://127.0.0.1:6334",
    string? ApiKey = null,
    bool TrustNonLoopbackEndpoint = false,
    bool RequireTlsForRemoteEndpoint = true,
    bool UseLocalBundledServer = true,
    int LocalGrpcPort = 6334,
    int RequestTimeoutSeconds = 30);

public sealed record QdrantSettingsResponse(
    string GrpcEndpoint,
    bool HasApiKey,
    bool TrustNonLoopbackEndpoint,
    bool RequireTlsForRemoteEndpoint,
    bool UseLocalBundledServer,
    int LocalGrpcPort,
    int RequestTimeoutSeconds);

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
    string? Error);
