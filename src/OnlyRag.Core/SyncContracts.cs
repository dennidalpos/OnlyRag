namespace OnlyRag.Core;

public enum LanDeviceStatus
{
    Discovered,
    PairingRequested,
    Authorized,
    Revoked
}

public sealed record LanDeviceDescriptor(
    string DeviceId,
    string DeviceName,
    string IpAddress,
    int Port,
    LanDeviceStatus Status,
    DateTimeOffset AuthorizedAtUtc,
    string PublicKeyPem);

public sealed record SyncPairingRequest(
    string RequestId,
    string SourceDeviceId,
    string SourceDeviceName,
    string PublicKeyPem,
    string PinCode);

public sealed record SyncSnapshotManifest(
    string SnapshotId,
    string SourceDeviceId,
    long DatabaseSizeBytes,
    long VectorCount,
    string ChecksumSha256,
    DateTimeOffset CreatedAtUtc,
    bool IsEncrypted = true);

public sealed record SyncConflictItem(
    string ConflictId,
    string EntityType,
    string EntityId,
    string LocalStateJson,
    string RemoteStateJson,
    DateTimeOffset DetectedAtUtc);

public interface ILanSyncService
{
    Task<IReadOnlyList<LanDeviceDescriptor>> GetAuthorizedDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LanDeviceDescriptor>> DiscoverLanNodesAsync(CancellationToken cancellationToken = default);
    Task AnnounceNodeBeaconAsync(CancellationToken cancellationToken = default);
    Task<SyncPairingRequest> InitiatePairingAsync(string targetIp, int port, string pinCode, CancellationToken cancellationToken = default);
    Task<bool> ApprovePairingAsync(string requestId, CancellationToken cancellationToken = default);
    Task RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<SyncSnapshotManifest> CreateEncryptedSnapshotAsync(string outputPath, CancellationToken cancellationToken = default);
    Task RestoringSnapshotAsync(string snapshotPath, string decryptionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncConflictItem>> GetConflictsAsync(CancellationToken cancellationToken = default);
    Task ResolveConflictAsync(string conflictId, bool keepLocal, CancellationToken cancellationToken = default);
}
