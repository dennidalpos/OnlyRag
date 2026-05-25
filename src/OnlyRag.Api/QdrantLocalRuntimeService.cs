using System.Diagnostics;
using System.Text;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Api;

internal sealed class QdrantLocalRuntimeService
{
    private const string QdrantExeName = "qdrant.exe";
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
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--config-path");
        startInfo.ArgumentList.Add(ResolveConfigPath());

        Process process = new() { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Qdrant non ha accettato la richiesta di avvio.");
        }

        await File.WriteAllTextAsync(ResolvePidPath(), process.Id.ToString(), cancellationToken);
        return await GetStatusAsync(vectorStore, cancellationToken);
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
            File.Delete(ResolvePidPath());
        }

        return Task.CompletedTask;
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

    private static string ToYamlPath(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
