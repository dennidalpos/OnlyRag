using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Tests;

public sealed class OnnxCrossEncoderReRankerServiceTests
{
    [Fact]
    public async Task ReRankAsync_WhenModelMissing_FallsBackToHeuristicReRanker()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-reranker-tests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);
        RerankerModelManager modelManager = new(paths);
        using OnnxCrossEncoderReRankerService service = new(modelManager);

        List<ReRankCandidate> candidates = new()
        {
            new ReRankCandidate(1, "Questo documento parla di contratti e fatture."),
            new ReRankCandidate(2, "Questo documento riguarda tutt'altro argomento.")
        };

        IReadOnlyList<ReRankResult> results = await service.ReRankAsync("contratti", candidates);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].ChunkId);
        Assert.True(results[0].Score > results[1].Score);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReRankAsync_EmptyCandidates_ReturnsEmptyList()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-reranker-tests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);
        RerankerModelManager modelManager = new(paths);
        using OnnxCrossEncoderReRankerService service = new(modelManager);

        IReadOnlyList<ReRankResult> results = await service.ReRankAsync("test", []);
        Assert.Empty(results);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RerankerModelManager_GetStatus_ReturnsDefaultModelInfo()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-reranker-tests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);
        RerankerModelManager modelManager = new(paths);

        RerankerModelInfo status = await modelManager.GetModelStatusAsync();

        Assert.Equal(RerankerModelManager.DefaultModelId, status.Id);
        Assert.False(status.IsDownloaded);
        Assert.False(status.IsDownloading);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RerankerModelManager_DeleteModel_HandlesMissingFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "OnlyRag-reranker-tests", Guid.NewGuid().ToString("N"));
        AppStoragePaths paths = AppStoragePaths.FromRoot(root);
        RerankerModelManager modelManager = new(paths);

        bool deleted = await modelManager.DeleteModelAsync();
        Assert.False(deleted);

        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }
}
