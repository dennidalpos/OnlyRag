using OnlyRag.Api;

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
}
