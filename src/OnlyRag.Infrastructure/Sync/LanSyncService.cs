using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Sync;

public sealed class LanSyncService : ILanSyncService
{
    private const string MulticastGroupAddress = "224.0.0.251";
    private const int MulticastPort = 5353;

    private readonly ConcurrentDictionary<string, LanDeviceDescriptor> _devices = new();
    private readonly ConcurrentDictionary<string, SyncPairingRequest> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, SyncConflictItem> _conflicts = new();
    private readonly string _localDeviceId = $"node_{Guid.NewGuid():N}";
    private readonly string _localDeviceName = Environment.MachineName;

    public Task<IReadOnlyList<LanDeviceDescriptor>> GetAuthorizedDevicesAsync(CancellationToken cancellationToken = default)
    {
        var list = _devices.Values.Where(d => d.Status == LanDeviceStatus.Authorized).ToList();
        return Task.FromResult<IReadOnlyList<LanDeviceDescriptor>>(list);
    }

    public async Task<IReadOnlyList<LanDeviceDescriptor>> DiscoverLanNodesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await AnnounceNodeBeaconAsync(cancellationToken).ConfigureAwait(false);

            using var udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.ReceiveTimeout = 300;
            udpClient.ExclusiveAddressUse = false;

            var localEp = new IPEndPoint(IPAddress.Any, MulticastPort);
            udpClient.Client.Bind(localEp);
            udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastGroupAddress));

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(300);

            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await udpClient.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    string json = Encoding.UTF8.GetString(result.Buffer);

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("deviceId", out var idProp) && root.TryGetProperty("deviceName", out var nameProp))
                    {
                        string remoteId = idProp.GetString() ?? string.Empty;
                        string remoteName = nameProp.GetString() ?? string.Empty;

                        if (!string.IsNullOrEmpty(remoteId) && remoteId != _localDeviceId)
                        {
                            var descriptor = new LanDeviceDescriptor(
                                remoteId,
                                remoteName,
                                result.RemoteEndPoint.Address.ToString(),
                                MulticastPort,
                                LanDeviceStatus.Discovered,
                                DateTimeOffset.UtcNow,
                                string.Empty);

                            _devices[remoteId] = descriptor;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore socket receive timeouts or invalid frames
                }
            }
        }
        catch
        {
            // UDP multicast socket bind fallback
        }

        var discovered = _devices.Values.Where(d => d.Status == LanDeviceStatus.Discovered || d.Status == LanDeviceStatus.Authorized).ToList();
        return discovered;
    }

    public async Task AnnounceNodeBeaconAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var udpClient = new UdpClient();
            var targetEp = new IPEndPoint(IPAddress.Parse(MulticastGroupAddress), MulticastPort);
            udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastGroupAddress));

            var payload = new
            {
                deviceId = _localDeviceId,
                deviceName = _localDeviceName,
                service = "OnlyRag-mDNS",
                timestamp = DateTimeOffset.UtcNow
            };

            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            await udpClient.SendAsync(bytes, bytes.Length, targetEp).ConfigureAwait(false);
        }
        catch
        {
            // Ignore multicast send errors on isolated interfaces
        }
    }

    public Task<SyncPairingRequest> InitiatePairingAsync(
        string targetIp,
        int port,
        string pinCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIp);
        ArgumentException.ThrowIfNullOrWhiteSpace(pinCode);

        string reqId = $"pair_{Guid.NewGuid():N}";
        string devId = $"dev_{Guid.NewGuid():N}";

        using var rsa = RSA.Create(2048);
        string pubKeyPem = rsa.ExportRSAPublicKeyPem();

        var request = new SyncPairingRequest(reqId, devId, $"Device_{targetIp}", pubKeyPem, pinCode);
        _pendingRequests[reqId] = request;

        return Task.FromResult(request);
    }

    public Task<bool> ApprovePairingAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (!_pendingRequests.TryRemove(requestId, out var request))
        {
            return Task.FromResult(false);
        }

        var device = new LanDeviceDescriptor(
            request.SourceDeviceId,
            request.SourceDeviceName,
            "127.0.0.1",
            8080,
            LanDeviceStatus.Authorized,
            DateTimeOffset.UtcNow,
            request.PublicKeyPem);

        _devices[device.DeviceId] = device;
        return Task.FromResult(true);
    }

    public Task RevokeDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (_devices.TryGetValue(deviceId, out var dev))
        {
            _devices[deviceId] = dev with { Status = LanDeviceStatus.Revoked };
        }
        return Task.CompletedTask;
    }

    public async Task<SyncSnapshotManifest> CreateEncryptedSnapshotAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string snapshotId = $"snap_{Guid.NewGuid():N}";

        byte[] rawData = Encoding.UTF8.GetBytes($"OnlyRag Snapshot Payload v1 - {now:O}");
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cryptoStream = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            await cryptoStream.WriteAsync(rawData, cancellationToken);
        }

        byte[] encryptedBytes = ms.ToArray();

        string directory = Path.GetDirectoryName(outputPath) ?? ".";
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(outputPath, encryptedBytes, cancellationToken);

        byte[] hash = SHA256.HashData(encryptedBytes);
        string checksum = Convert.ToHexString(hash).ToLowerInvariant();

        var manifest = new SyncSnapshotManifest(
            snapshotId,
            _localDeviceId,
            encryptedBytes.Length,
            1,
            checksum,
            now,
            IsEncrypted: true);

        return manifest;
    }

    public async Task RestoringSnapshotAsync(string snapshotPath, string decryptionKey, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException("Snapshot file not found", snapshotPath);
        }

        byte[] encryptedData = await File.ReadAllBytesAsync(snapshotPath, cancellationToken);
        if (encryptedData.Length < 16)
        {
            throw new InvalidOperationException("Invalid encrypted snapshot format.");
        }

        await Task.Yield();
    }

    public Task<IReadOnlyList<SyncConflictItem>> GetConflictsAsync(CancellationToken cancellationToken = default)
    {
        var list = _conflicts.Values.ToList();
        return Task.FromResult<IReadOnlyList<SyncConflictItem>>(list);
    }

    public Task ResolveConflictAsync(string conflictId, bool keepLocal, CancellationToken cancellationToken = default)
    {
        _conflicts.TryRemove(conflictId, out _);
        return Task.CompletedTask;
    }
}
