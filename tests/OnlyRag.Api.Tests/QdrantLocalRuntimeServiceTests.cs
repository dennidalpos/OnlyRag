using OnlyRag.Api;
using OnlyRag.Core;

namespace OnlyRag.Api.Tests;

public sealed class QdrantLocalRuntimeServiceTests
{
    [Fact]
    public void FindQdrantBinary_finds_payload_binary_from_repo_root_candidates()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "onlyrag-qdrant-tests", Guid.NewGuid().ToString("N"));
        string payloadRoot = Path.Combine(tempRoot, "packaging", "qdrant", "payload");
        Directory.CreateDirectory(payloadRoot);
        string expectedBinary = Path.Combine(payloadRoot, "qdrant.exe");
        File.WriteAllText(expectedBinary, "stub");

        try
        {
            string? resolved = QdrantLocalRuntimeService.FindQdrantBinary(tempRoot);
            Assert.Equal(expectedBinary, resolved);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void FindQdrantBinary_uses_configured_override_when_present()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "onlyrag-qdrant-tests", Guid.NewGuid().ToString("N"));
        string overrideRoot = Path.Combine(tempRoot, "custom-qdrant");
        Directory.CreateDirectory(overrideRoot);
        string expectedBinary = Path.Combine(overrideRoot, "qdrant.exe");
        File.WriteAllText(expectedBinary, "stub");

        try
        {
            string? resolved = QdrantLocalRuntimeService.FindQdrantBinary(tempRoot, overrideRoot);
            Assert.Equal(expectedBinary, resolved);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(6334, 6333)]
    [InlineData(6333, 6335)]
    public void GetLocalHttpPort_avoids_the_configured_gRpc_port(int grpcPort, int expectedHttpPort)
    {
        Assert.Equal(expectedHttpPort, QdrantLocalRuntimeService.GetLocalHttpPort(grpcPort));
    }

    [Theory]
    [InlineData(true, true, 6334, 6335, true)]
    [InlineData(true, false, 6334, 6334, true)]
    [InlineData(false, false, 6334, 6335, false)]
    public void RequiresLocalRuntimeRestart_detects_local_runtime_changes(
        bool previousUsesLocalRuntime,
        bool currentUsesLocalRuntime,
        int previousGrpcPort,
        int currentGrpcPort,
        bool expected)
    {
        QdrantSettings previous = new(
            GrpcEndpoint: $"http://127.0.0.1:{previousGrpcPort}",
            UseLocalBundledServer: previousUsesLocalRuntime,
            LocalGrpcPort: previousGrpcPort);
        QdrantSettings current = new(
            GrpcEndpoint: $"http://127.0.0.1:{currentGrpcPort}",
            UseLocalBundledServer: currentUsesLocalRuntime,
            LocalGrpcPort: currentGrpcPort);

        Assert.Equal(expected, QdrantLocalRuntimeService.RequiresLocalRuntimeRestart(previous, current));
    }
}
