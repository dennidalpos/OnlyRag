namespace OnlyRag.Api;

internal sealed record VectorBackendHealthResponse(
    string BackendName,
    bool StoragePersistent,
    int VectorLimit,
    int TotalVectors,
    bool NearLimit,
    string? Warning);
