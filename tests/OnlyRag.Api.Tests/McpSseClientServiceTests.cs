using OnlyRag.Api.Mcp;
using OnlyRag.Core;
using OnlyRag.Core.Mcp;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class McpSseClientServiceTests : IDisposable
{
    private readonly string _testTempDir;

    public McpSseClientServiceTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"onlyrag_mcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    [Fact]
    public void GetSessionStatus_ReturnsDisconnectedForUnknownServer()
    {
        using var httpClient = new HttpClient();
        var storagePaths = AppStoragePaths.FromRoot(_testTempDir);
        using var service = new McpSseClientService(httpClient, storagePaths);

        McpSseSessionStatus status = service.GetSessionStatus("non_existent_server");

        Assert.NotNull(status);
        Assert.Equal(McpSseConnectionState.Disconnected, status.State);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempDir))
        {
            try { Directory.Delete(_testTempDir, recursive: true); } catch { }
        }
    }
}
