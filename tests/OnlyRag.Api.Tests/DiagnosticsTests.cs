using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json;
using OnlyRag.Api;
using OnlyRag.Core;
using OnlyRag.Infrastructure;
using OnlyRag.Worker;

namespace OnlyRag.Api.Tests;

public sealed class DiagnosticsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Diagnostics_ReturnsExpectedPathsAndVersion()
    {
        using TempDiagDescriptor temp = TempDiagDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(temp.Descriptor);
        using HttpClient httpClient = new() { BaseAddress = backend.BaseUri };

        DiagnosticsResponse? diag = await httpClient.GetFromJsonAsync<DiagnosticsResponse>("/api/diagnostics", JsonOptions);

        Assert.NotNull(diag);
        Assert.Equal(temp.Descriptor.StoragePaths.DatabasePath, diag.DatabasePath);
        Assert.Equal(temp.Descriptor.StoragePaths.LogsDirectory, diag.LogsDirectory);
        Assert.False(string.IsNullOrWhiteSpace(diag.AppVersion));
        Assert.False(string.IsNullOrWhiteSpace(diag.OcrEngineName));
    }

    [Fact]
    public async Task Diagnostics_WithUnreachableOllama_ReportsOffline()
    {
        using TempDiagDescriptor temp = TempDiagDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(temp.Descriptor);
        using HttpClient httpClient = new() { BaseAddress = backend.BaseUri };

        await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            new OllamaSettings("http://127.0.0.1:1", null, null, null, 5, 1));

        DiagnosticsResponse? diag = await httpClient.GetFromJsonAsync<DiagnosticsResponse>("/api/diagnostics", JsonOptions);

        Assert.NotNull(diag);
        Assert.False(diag.OllamaIsReachable);
        Assert.Equal("Offline", diag.OllamaStatus);
    }

    [Fact]
    public async Task OllamaModels_WithUnreachableUrl_Returns503()
    {
        using TempDiagDescriptor temp = TempDiagDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(temp.Descriptor);
        using HttpClient httpClient = new() { BaseAddress = backend.BaseUri };

        await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            new OllamaSettings("http://127.0.0.1:1", null, null, null, 5, 1));

        using HttpResponseMessage response = await httpClient.GetAsync("/api/ollama/models");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task OllamaModels_ErrorResponse_DoesNotContainStackTrace()
    {
        using TempDiagDescriptor temp = TempDiagDescriptor.Create();
        await using InProcessBackendHandle backend = await InProcessBackend.StartAsync(temp.Descriptor);
        using HttpClient httpClient = new() { BaseAddress = backend.BaseUri };

        await httpClient.PutAsJsonAsync(
            "/api/settings/ollama",
            new OllamaSettings("http://127.0.0.1:1", null, null, null, 5, 1));

        using HttpResponseMessage response = await httpClient.GetAsync("/api/ollama/models");
        string body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("at OnlyRag", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendLog_WritesToLogsDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.DiagTests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);

        try
        {
            BackendLog.Write(paths, "test log entry");

            string logFile = Path.Combine(paths.LogsDirectory, "backend.log");
            Assert.True(File.Exists(logFile));
            string content = File.ReadAllText(logFile);
            Assert.Contains("test log entry", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BackendLog_WriteWithCorrelationId_IncludesIdInEntry()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.DiagTests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);

        try
        {
            BackendLog.Write(paths, "corr-id-abc123", "operation started");

            string content = File.ReadAllText(Path.Combine(paths.LogsDirectory, "backend.log"));
            Assert.Contains("corr-id-abc123", content, StringComparison.Ordinal);
            Assert.Contains("operation started", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BackendLog_WriteException_LogsTypeAndMessageWithoutFullStackTrace()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.DiagTests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);

        try
        {
            Exception ex;
            try
            {
                throw new InvalidOperationException("database unavailable");
            }
            catch (Exception caught)
            {
                ex = caught;
            }

            BackendLog.WriteException(paths, "job-xyz", "processing failed", ex);

            string content = File.ReadAllText(Path.Combine(paths.LogsDirectory, "backend.log"));
            Assert.Contains("InvalidOperationException", content, StringComparison.Ordinal);
            Assert.Contains("database unavailable", content, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BackendLog_RotatesFileWhenSizeExceedsLimit()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag.DiagTests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);

        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            string logPath = Path.Combine(paths.LogsDirectory, "backend.log");

            // Write 6MB to trigger rotation (limit is 5MB).
            File.WriteAllBytes(logPath, new byte[6 * 1024 * 1024]);

            BackendLog.Write(paths, "entry after rotation");

            Assert.True(File.Exists($"{logPath}.1"), "Rotated file backend.log.1 should exist.");
            Assert.True(new FileInfo(logPath).Length < 1024 * 1024, "Active log should be small after rotation.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class TempDiagDescriptor : IDisposable
    {
        private TempDiagDescriptor(string root)
        {
            Root = root;
            AppStoragePaths paths = AppStoragePaths.FromRoot(root);
            Descriptor = new InProcessBackendDescriptor(
                paths,
                new LocalSqliteStoreDescriptor(paths),
                new LocalJobQueueDescriptor("diag-tests", Persistent: false, MaxParallelJobs: 1, MaxRetries: 0),
                new OllamaEndpointOptions());
        }

        public string Root { get; }
        public InProcessBackendDescriptor Descriptor { get; }

        public static TempDiagDescriptor Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "OnlyRag.DiagTests", Guid.NewGuid().ToString("N"));
            return new TempDiagDescriptor(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
