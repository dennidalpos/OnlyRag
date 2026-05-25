using System.Diagnostics;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

internal sealed class QdrantLocalRuntimeService
{
    private const string QdrantExeName = "qdrant.exe";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(500);
    private readonly InProcessBackendDescriptor descriptor;
    private readonly QdrantSettingsStore settingsStore;

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
            error);
    }

    public async Task<QdrantStatusResponse> StartAsync(
        IQdrantVectorStore vectorStore,
        CancellationToken cancellationToken = default)
    {
        QdrantSettings settings = await settingsStore.GetAsync(cancellationToken);
        QdrantStatusResponse currentStatus = await GetStatusAsync(vectorStore, cancellationToken);
        if (currentStatus.IsReachable)
        {
            return currentStatus;
        }

        int? existingPid = ReadPid();
        if (existingPid is not null && IsProcessRunning(existingPid.Value))
        {
            return await WaitForAvailabilityAsync(vectorStore, existingPid.Value, StartupTimeout, cancellationToken);
        }

        if (existingPid is not null)
        {
            DeletePidFile();
        }

        await StartLocalProcessAsync(settings, cancellationToken);
        int? startedPid = ReadPid();
        return await WaitForAvailabilityAsync(vectorStore, startedPid, StartupTimeout, cancellationToken);
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
                "qdrant.exe non trovato nel payload applicativo. Eseguire il packaging Qdrant prima dell'avvio locale.");
        }

        QdrantStatusResponse currentStatus = await GetStatusAsync(vectorStore, cancellationToken);
        if (currentStatus.IsReachable)
        {
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

    private QdrantStatusResponse CreateUnavailableStatus(
        QdrantSettings settings,
        Uri endpoint,
        string error)
    {
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
            error);
    }

    private async Task StartLocalProcessAsync(
        QdrantSettings settings,
        CancellationToken cancellationToken)
    {
        string? binary = ResolveBinaryPath();
        if (binary is null)
        {
            throw new InvalidOperationException("qdrant.exe non trovato nel payload applicativo. Eseguire il packaging Qdrant prima dell'avvio locale.");
        }

        WriteConfig(settings);
        Directory.CreateDirectory(ResolveStorageDirectory());
        Directory.CreateDirectory(ResolveLogDirectory());

        ProcessStartInfo startInfo = new()
        {
            FileName = binary,
            WorkingDirectory = Path.GetDirectoryName(binary) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--config-path");
        startInfo.ArgumentList.Add(ResolveConfigPath());

        Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Qdrant non ha accettato la richiesta di avvio.");
        }

        await File.WriteAllTextAsync(ResolvePidPath(), process.Id.ToString(), cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        int? pid = ReadPid();
        if (pid is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            using Process process = Process.GetProcessById(pid.Value);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
        }
        finally
        {
            DeletePidFile();
        }

        return Task.CompletedTask;
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
            if (pid is not null && !IsProcessRunning(pid.Value))
            {
                DeletePidFile();
                return await GetStatusAsync(vectorStore, cancellationToken);
            }

            await Task.Delay(StartupPollInterval, cancellationToken);
            lastStatus = await GetStatusAsync(vectorStore, cancellationToken);
        }

        return lastStatus.IsReachable
            ? lastStatus
            : lastStatus with { Error = lastStatus.Error ?? "Qdrant locale avviato ma non ancora raggiungibile sulla porta gRPC configurata." };
    }

    private static bool IsProcessRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string? BuildWarning(QdrantSettings settings, Uri endpoint)
    {
        if (!QdrantSettingsStore.IsLoopback(endpoint) && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            return "Endpoint Qdrant remoto senza TLS: usare solo su rete esplicitamente attendibile.";
        }

        if (!QdrantSettingsStore.IsLoopback(endpoint) && string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return "Endpoint Qdrant remoto senza API key configurata.";
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
