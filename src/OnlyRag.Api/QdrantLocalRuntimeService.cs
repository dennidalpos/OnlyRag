using System.Diagnostics;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

internal sealed class QdrantLocalRuntimeService : IAsyncDisposable
{
    private const string QdrantExeName = "qdrant.exe";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(500);
    private readonly InProcessBackendDescriptor descriptor;
    private readonly QdrantSettingsStore settingsStore;
    private readonly QdrantProcessSupervisor processSupervisor = new();

    private readonly SemaphoreSlim healingLock = new(1, 1);
    private CancellationTokenSource? autoHealingCts;
    private Task? autoHealingTask;

    public QdrantLocalRuntimeService(
        InProcessBackendDescriptor descriptor,
        QdrantSettingsStore settingsStore)
    {
        this.descriptor = descriptor;
        this.settingsStore = settingsStore;
    }

    public async Task<QdrantStatusResponse> GetStatusAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
        Uri endpoint = QdrantSettingsStore.ParseEndpoint(settings.GrpcEndpoint);
        string? error = null;
        bool reachable = false;
        try
        {
            await vectorStore.VerifyAvailabilityAsync(cancellationToken);
            reachable = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or Grpc.Core.RpcException)
        {
            error = ex.Message;
        }

        bool isHealingActive = autoHealingTask is { IsCompleted: false };
        return new QdrantStatusResponse(
            reachable ? "Online" : "Offline",
            reachable,
            settings.GrpcEndpoint,
            QdrantSettingsStore.IsLoopback(endpoint),
            endpoint.Scheme == Uri.UriSchemeHttps,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            Version: null,
            ResolveBinaryPath(),
            ResolveConfigPath(),
            ResolveStorageDirectory(),
            ReadPid(),
            BuildWarning(settings, endpoint),
            error,
            AutoHealingActive: isHealingActive,
            AutoHealRestartCount: processSupervisor.AutoHealRestartCount,
            LastAutoHealedAtUtc: processSupervisor.LastAutoHealedAtUtc);
    }

    public async Task<QdrantStatusResponse> StartAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
        QdrantStatusResponse currentStatus = await GetStatusAsync(vectorStore, cancellationToken);
        if (currentStatus.IsReachable)
        {
            TryAdoptPersistedProcess();
            StartAutoHealingSupervisor(vectorStore);
            return currentStatus;
        }

        int? existingPid = ReadPid();
        if (existingPid is not null && processSupervisor.IsOwnedProcess(existingPid.Value, ResolveBinaryPath()))
        {
            processSupervisor.TryAdoptProcess(existingPid.Value, ResolveBinaryPath());
            QdrantStatusResponse status = await WaitForAvailabilityAsync(vectorStore, existingPid.Value, StartupTimeout, cancellationToken);
            StartAutoHealingSupervisor(vectorStore);
            return status;
        }

        if (existingPid is not null)
        {
            DeletePidFile();
        }

        await StartLocalProcessAsync(settings, cancellationToken);
        int? startedPid = ReadPid();
        QdrantStatusResponse startedStatus = await WaitForAvailabilityAsync(vectorStore, startedPid, StartupTimeout, cancellationToken);
        StartAutoHealingSupervisor(vectorStore);
        return startedStatus;
    }

    public async Task<QdrantStatusResponse> EnsureLocalServerAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.UseLocalBundledServer)
        {
            return await GetStatusAsync(vectorStore, cancellationToken);
        }

        string? binary = ResolveBinaryPath();
        if (binary is null)
        {
            Uri endpoint = QdrantSettingsStore.ParseEndpoint(settings.GrpcEndpoint);
            return CreateUnavailableStatus(
                settings,
                endpoint,
                "qdrant.exe not found in the application payload. Run Qdrant packaging before local startup.");
        }

        QdrantStatusResponse currentStatus = await GetStatusAsync(vectorStore, cancellationToken);
        if (currentStatus.IsReachable)
        {
            TryAdoptPersistedProcess();
            StartAutoHealingSupervisor(vectorStore);
            return currentStatus;
        }

        try
        {
            return await StartAsync(vectorStore, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            QdrantStatusResponse status = await GetStatusAsync(vectorStore, cancellationToken);
            return status with { Error = ex.Message };
        }
    }

    public void StartAutoHealingSupervisor(IQdrantVectorStore vectorStore)
    {
        lock (processSupervisor)
        {
            if (autoHealingTask is { IsCompleted: false })
            {
                return;
            }

            autoHealingCts = new CancellationTokenSource();
            autoHealingTask = Task.Run(() => AutoHealingLoopAsync(vectorStore, autoHealingCts.Token));
        }
    }

    private async Task StopAutoHealingSupervisorAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (processSupervisor)
        {
            cts = autoHealingCts;
            task = autoHealingTask;
            autoHealingCts = null;
            autoHealingTask = null;
        }

        if (cts is not null)
        {
            cts.Cancel();
            if (task is not null)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException)
                {
                }
            }
            cts.Dispose();
        }
    }

    private async Task AutoHealingLoopAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

                QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
                if (!settings.UseLocalBundledServer)
                {
                    continue;
                }

                string? binary = ResolveBinaryPath();
                if (binary is null)
                {
                    continue;
                }

                int? pid = ReadPid();
                bool processAlive = pid is not null && processSupervisor.IsOwnedProcess(pid.Value, binary);

                bool gRpcReachable = false;
                if (processAlive)
                {
                    try
                    {
                        await vectorStore.VerifyAvailabilityAsync(cancellationToken);
                        gRpcReachable = true;
                    }
                    catch
                    {
                        gRpcReachable = false;
                    }
                }

                if (!processAlive || !gRpcReachable)
                {
                    BackendLog.Write(descriptor.StoragePaths, $"[Qdrant Auto-Healing] Sidecar process or gRPC endpoint unavailable (processAlive={processAlive}, gRpcReachable={gRpcReachable}). Restarting sidecar...");

                    await healingLock.WaitAsync(cancellationToken);
                    try
                    {
                        await processSupervisor.StopAsync(ReadPid(), ResolveBinaryPath(), cancellationToken);
                        DeletePidFile();

                        await StartLocalProcessAsync(settings, cancellationToken);
                        int? startedPid = ReadPid();
                        await WaitForAvailabilityAsync(vectorStore, startedPid, StartupTimeout, cancellationToken);

                        processSupervisor.RecordAutoHeal();
                        BackendLog.Write(descriptor.StoragePaths, $"[Qdrant Auto-Healing] Qdrant sidecar auto-healed (Restart Count: {processSupervisor.AutoHealRestartCount}).");
                    }
                    catch (Exception ex)
                    {
                        BackendLog.Write(descriptor.StoragePaths, $"[Qdrant Auto-Healing] Auto-healing attempt failed: {ex.Message}");
                    }
                    finally
                    {
                        healingLock.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                BackendLog.Write(descriptor.StoragePaths, $"[Qdrant Auto-Healing] Supervisor loop error: {ex.Message}");
            }
        }
    }

    private QdrantStatusResponse CreateUnavailableStatus(
        QdrantSettings settings,
        Uri endpoint,
        string error)
    {
        bool isHealingActive = autoHealingTask is { IsCompleted: false };
        return new QdrantStatusResponse(
            "Offline",
            false,
            settings.GrpcEndpoint,
            QdrantSettingsStore.IsLoopback(endpoint),
            endpoint.Scheme == Uri.UriSchemeHttps,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            Version: null,
            ResolveBinaryPath(),
            ResolveConfigPath(),
            ResolveStorageDirectory(),
            ReadPid(),
            BuildWarning(settings, endpoint),
            error,
            AutoHealingActive: isHealingActive,
            AutoHealRestartCount: processSupervisor.AutoHealRestartCount,
            LastAutoHealedAtUtc: processSupervisor.LastAutoHealedAtUtc);
    }

    private async Task StartLocalProcessAsync(
        QdrantSettings settings,
        CancellationToken cancellationToken)
    {
        string? binary = ResolveBinaryPath();
        if (binary is null)
        {
            throw new InvalidOperationException("qdrant.exe not found in the application payload. Run Qdrant packaging before local startup.");
        }

        WriteConfig(settings);
        Directory.CreateDirectory(ResolveStorageDirectory());
        Directory.CreateDirectory(ResolveLogDirectory());
        Directory.CreateDirectory(ResolveRuntimeDirectory());

        ProcessStartInfo startInfo = new()
        {
            FileName = binary,
            WorkingDirectory = ResolveRuntimeDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--config-path");
        startInfo.ArgumentList.Add(ResolveConfigPath());

        Process process = new() { StartInfo = startInfo, EnableRaisingEvents = true };
        bool attached = false;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Qdrant did not accept the start request.");
            }

            processSupervisor.AttachStartedProcess(process);
            attached = true;

            await File.WriteAllTextAsync(ResolvePidPath(), process.Id.ToString(), cancellationToken);
            BackendLog.Write(descriptor.StoragePaths, $"Qdrant local runtime process started with pid {process.Id}.");
        }
        catch
        {
            if (attached)
            {
                processSupervisor.DetachStartedProcess(process);
            }

            QdrantProcessSupervisor.KillAndDisposeProcess(process);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await StopAutoHealingSupervisorAsync();
        try
        {
            await processSupervisor.StopAsync(ReadPid(), ResolveBinaryPath(), cancellationToken);
        }
        finally
        {
            DeletePidFile();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await processSupervisor.DisposeAsync();
        healingLock.Dispose();
    }

    private async Task<QdrantStatusResponse> WaitForAvailabilityAsync(
        IQdrantVectorStore vectorStore,
        int? pid,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        QdrantStatusResponse lastStatus = await GetStatusAsync(vectorStore, cancellationToken);
        while (!lastStatus.IsReachable && stopwatch.Elapsed < timeout)
        {
            if (pid is not null && !processSupervisor.IsOwnedProcess(pid.Value, ResolveBinaryPath()))
            {
                DeletePidFile();
                return await GetStatusAsync(vectorStore, cancellationToken);
            }

            await Task.Delay(StartupPollInterval, cancellationToken);
            lastStatus = await GetStatusAsync(vectorStore, cancellationToken);
        }

        return lastStatus.IsReachable
            ? lastStatus
            : lastStatus with { Error = lastStatus.Error ?? "Local Qdrant started but not yet reachable on the configured gRPC port." };
    }

    private void TryAdoptPersistedProcess()
    {
        int? pid = ReadPid();
        if (pid is not null)
        {
            processSupervisor.TryAdoptProcess(pid.Value, ResolveBinaryPath());
        }
    }

    private static string? BuildWarning(QdrantSettings settings, Uri endpoint)
    {
        if (!QdrantSettingsStore.IsLoopback(endpoint) && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return "Remote Qdrant endpoint without TLS: use only on an explicitly trusted network.";
        }

        if (!QdrantSettingsStore.IsLoopback(endpoint) && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return "Remote Qdrant endpoint without an API key configured.";
        }

        return null;
    }

    private void WriteConfig(QdrantSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResolveConfigPath())!);
        int httpPort = settings.LocalGrpcPort == 6333 ? 6335 : 6333;
        string config = $$"""
            service:
              host: 127.0.0.1
              http_port: {{httpPort}}
              grpc_port: {{settings.LocalGrpcPort}}
            storage:
              storage_path: "{{ToYamlPath(ResolveStorageDirectory())}}"
            log_level: INFO
            """;
        File.WriteAllText(ResolveConfigPath(), config, Encoding.UTF8);
    }

    private string? ResolveBinaryPath()
    {
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "qdrant", QdrantExeName),
            Path.Combine(AppContext.BaseDirectory, QdrantExeName)
        ];

        return candidates.FirstOrDefault(File.Exists);
    }

    private string ResolveConfigPath()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "qdrant", "config", "qdrant.yaml");
    }

    private string ResolveStorageDirectory()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "qdrant", "storage");
    }

    private string ResolveLogDirectory()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "qdrant", "logs");
    }

    private string ResolveRuntimeDirectory()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "qdrant", "runtime");
    }

    private string ResolvePidPath()
    {
        return Path.Combine(descriptor.StoragePaths.DataRoot, "qdrant", "qdrant.pid");
    }

    private int? ReadPid()
    {
        string pidPath = ResolvePidPath();
        if (!File.Exists(pidPath))
        {
            return null;
        }

        string content = File.ReadAllText(pidPath);
        return int.TryParse(content, out int pid) ? pid : null;
    }

    private void DeletePidFile()
    {
        string pidPath = ResolvePidPath();
        if (File.Exists(pidPath))
        {
            File.Delete(pidPath);
        }
    }

    private static string ToYamlPath(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
