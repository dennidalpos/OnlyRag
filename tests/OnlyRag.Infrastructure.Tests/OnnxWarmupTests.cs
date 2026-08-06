using OnlyRag.Infrastructure.Images;
using OnlyRag.Infrastructure.Retrieval;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class OnnxWarmupTests
{
    [Fact]
    public async Task WarmupAsync_HeuristicReRanker_CompletesSuccessfully()
    {
        var reranker = new HeuristicReRankerService();
        await reranker.WarmupAsync();
        Assert.True(true);
    }

    [Fact]
    public async Task WarmupAsync_OnnxImageEngine_NonExistentDirectory_CompletesGracefully()
    {
        using var engine = new OnnxStableDiffusionImageGenerationEngine();
        string nonExistent = Path.Combine(Path.GetTempPath(), $"non_existent_models_{Guid.NewGuid():N}");
        await engine.WarmupAsync(nonExistent, preferGpu: false);
        Assert.False(engine.GetStatus().IsInitialized);
    }
}
