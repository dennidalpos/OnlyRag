using System.Diagnostics;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

internal sealed class QdrantLocalRuntimeService : IAsyncDisposable
{
    private const string QdrantExeName = "qdrant.exe";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AvailabilityProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HealthFailureGracePeriod = TimeSpan.FromSeconds(30);
    private readonly InProcessBackendDescriptor descriptor;
    private readonly QdrantSettingsStore settingsStore;
    private readonly QdrantProcessSupervisor processSupervisor = new();

    private readonly SemaphoreSlim healingLock = new(1, 1);
    private readonly SemaphoreSlim startupLock = new(1, 1);
    private CancellationTokenSource? autoHealingCts;
    private Task? autoHealingTask;
    private volatile bool isStartupInProgress;

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
        string? binaryPath = ResolveBinaryPath();
        if (settings.UseLocalBundledServer && binaryPath is null)
        {
            return CreateUnavailableStatus(
                settings,
                endpoint,
                "Runtime Qdrant locale non installato: qdrant.exe non è presente nell'output dell'app. Esegui scripts\\Download-Qdrant.ps1 e ricompila.");
        }

        string? error = null;
        bool reachable = false;
        try
        {
            using CancellationTokenSource probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(AvailabilityProbeTimeout);
            reachable = await VerifyAvailabilityAsync(vectorStore, probeCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            error = $"Qdrant availability probe timed out after {AvailabilityProbeTimeout.TotalSeconds:0} seconds.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or Grpc.Core.RpcException)
        {
            error = ex.Message;
        }

        bool isHealingActive = autoHealingTask is { IsCompleted: false };
        bool isStarting = isStartupInProgress;
        string statusText = reachable
            ? "Online"
            : isStarting
                ? "Caricamento"
                : "Offline";

        return new QdrantStatusResponse(
            statusText,
            reachable,
            settings.GrpcEndpoint,
            QdrantSettingsStore.IsLoopback(endpoint),
            endpoint.Scheme == Uri.UriSchemeHttps,
            !string.IsNullOrWhiteSpace(settings.ApiKey),
            Version: null,
            binaryPath,
            ResolveConfigPath(),
            ResolveStorageDirectory(),
            ReadPid(),
            BuildWarning(settings, endpoint),
            reachable || isStarting ? null : error,
            AutoHealingActive: isHealingActive,
            AutoHealRestartCount: processSupervisor.AutoHealRestartCount,
            LastAutoHealedAtUtc: processSupervisor.LastAutoHealedAtUtc);
    }

    public async Task<QdrantStatusResponse> StartAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        await startupLock.WaitAsync(cancellationToken);
        try
        {
            return await StartCoreAsync(vectorStore, cancellationToken);
        }
        finally
        {
            startupLock.Release();
        }
    }

    private async Task<QdrantStatusResponse> StartCoreAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.UseLocalBundledServer)
        {
            throw new InvalidOperationException("L'avvio locale di Qdrant è disabilitato nelle impostazioni.");
        }

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
            isStartupInProgress = true;
            try
            {
                QdrantStatusResponse status = await WaitForAvailabilityAsync(vectorStore, existingPid.Value, StartupTimeout, cancellationToken);
                if (status.IsReachable)
                {
                    StartAutoHealingSupervisor(vectorStore);
                }
                return status;
            }
            finally
            {
                isStartupInProgress = false;
            }
        }

        if (existingPid is not null)
        {
            DeletePidFile();
        }

        isStartupInProgress = true;
        try
        {
            await StartLocalProcessAsync(settings, cancellationToken);
            int? startedPid = ReadPid();
            QdrantStatusResponse startedStatus = await WaitForAvailabilityAsync(vectorStore, startedPid, StartupTimeout, cancellationToken);
            if (startedStatus.IsReachable)
            {
                StartAutoHealingSupervisor(vectorStore);
            }
            return startedStatus;
        }
        finally
        {
            isStartupInProgress = false;
        }
    }

    public async Task<QdrantStatusResponse> EnsureLocalServerAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        await startupLock.WaitAsync(cancellationToken);
        try
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
                return await StartCoreAsync(vectorStore, cancellationToken);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                QdrantStatusResponse status = await GetStatusAsync(vectorStore, cancellationToken);
                return status with { Error = ex.Message };
            }
        }
        finally
        {
            startupLock.Release();
        }
    }

    public async Task<QdrantStatusResponse> ApplySettingsAsync(
        QdrantSettings previousSettings,
        QdrantSettings currentSettings,
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        if (!RequiresLocalRuntimeRestart(previousSettings, currentSettings))
        {
            return await GetStatusAsync(vectorStore, cancellationToken);
        }

        await startupLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
            return currentSettings.UseLocalBundledServer
                ? await StartCoreAsync(vectorStore, cancellationToken)
                : await GetStatusAsync(vectorStore, cancellationToken);
        }
        finally
        {
            startupLock.Release();
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
        DateTimeOffset? gRpcUnavailableSinceUtc = null;
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
                        Uri endpoint = QdrantSettingsStore.ParseEndpoint(settings.GrpcEndpoint);
                        gRpcReachable = await VerifyAvailabilityAsync(vectorStore, cancellationToken);
                        gRpcUnavailableSinceUtc = null;
                    }
                    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException or Grpc.Core.RpcException)
                    {
                        gRpcReachable = false;
                        gRpcUnavailableSinceUtc ??= DateTimeOffset.UtcNow;
                    }
                }

                bool healthFailureExpired = gRpcUnavailableSinceUtc is not null
                    && DateTimeOffset.UtcNow - gRpcUnavailableSinceUtc >= HealthFailureGracePeriod;
                if (!processAlive || (!gRpcReachable && healthFailureExpired && !isStartupInProgress))
                {
                    BackendLog.Write(descriptor.StoragePaths, $"[Qdrant Auto-Healing] Sidecar process or gRPC endpoint unavailable (processAlive={processAlive}, gRpcReachable={gRpcReachable}). Restarting sidecar...");

                    await healingLock.WaitAsync(cancellationToken);
                    try
                    {
                        gRpcUnavailableSinceUtc = null;
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
        await startupLock.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            startupLock.Release();
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
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
        startupLock.Dispose();
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

    private static async Task<bool> VerifyAvailabilityAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken)
    {
        await vectorStore.VerifyAvailabilityAsync(cancellationToken);
        return true;
    }

    internal static bool RequiresLocalRuntimeRestart(
        QdrantSettings previousSettings,
        QdrantSettings currentSettings)
    {
        return previousSettings.UseLocalBundledServer || currentSettings.UseLocalBundledServer
            ? previousSettings.UseLocalBundledServer != currentSettings.UseLocalBundledServer
                || previousSettings.LocalGrpcPort != currentSettings.LocalGrpcPort
                || !string.Equals(previousSettings.GrpcEndpoint, currentSettings.GrpcEndpoint, StringComparison.OrdinalIgnoreCase)
            : false;
    }

    internal static int GetLocalHttpPort(int grpcPort)
    {
        return grpcPort == 6333 ? 6335 : 6333;
    }

    private void WriteConfig(QdrantSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResolveConfigPath())!);
        int httpPort = GetLocalHttpPort(settings.LocalGrpcPort);
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
        return FindQdrantBinary(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("ONLYRAG_QDRANT_PATH") ?? Environment.GetEnvironmentVariable("QDRANT_PATH"));
    }

    internal static string? FindQdrantBinary(string? baseDirectory, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            foreach (string candidate in EnumerateConfiguredBinaryCandidates(configuredPath))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        foreach (string candidate in EnumerateBinaryCandidates(baseDirectory))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (string candidate in EnumeratePathBinaryCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBinaryCandidates(string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield break;
        }

        foreach (string root in EnumerateSearchRoots(baseDirectory))
        {
            yield return Path.Combine(root, "qdrant", QdrantExeName);
            yield return Path.Combine(root, QdrantExeName);
            yield return Path.Combine(root, "packaging", "qdrant", "payload", QdrantExeName);
            yield return Path.Combine(root, "payload", "qdrant", QdrantExeName);
            yield return Path.Combine(root, "payload", QdrantExeName);
        }
    }

    private static IEnumerable<string> EnumerateConfiguredBinaryCandidates(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            yield break;
        }

        if (File.Exists(configuredPath))
        {
            yield return configuredPath;
            yield break;
        }

        if (Directory.Exists(configuredPath))
        {
            yield return Path.Combine(configuredPath, QdrantExeName);
            yield return Path.Combine(configuredPath, "qdrant", QdrantExeName);
            yield return Path.Combine(configuredPath, "packaging", "qdrant", "payload", QdrantExeName);
            yield return Path.Combine(configuredPath, "payload", QdrantExeName);
        }
    }

    private static IEnumerable<string> EnumeratePathBinaryCandidates()
    {
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            yield break;
        }

        foreach (string pathEntry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return Path.Combine(pathEntry, QdrantExeName);
            yield return Path.Combine(pathEntry, "qdrant", QdrantExeName);
        }
    }

    private static IEnumerable<string> EnumerateSearchRoots(string baseDirectory)
    {
        DirectoryInfo? current = new(baseDirectory);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
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
