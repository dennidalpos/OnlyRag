using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Infrastructure.Tests;

public sealed class NextGenRagPipelineTests
{
    [Fact]
    public async Task ReRanker_CalculatesCrossEncoderScores()
    {
        HeuristicReRankerService reRanker = new();
        List<ReRankCandidate> candidates = new()
        {
            new ReRankCandidate(1, "Testo generico irrilevante"),
            new ReRankCandidate(2, "Contratto siglato in data 2026 per la fornitura di servizi RAG.")
        };

        IReadOnlyList<ReRankResult> results = await reRanker.ReRankAsync("fornitura servizi RAG 2026", candidates);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].ChunkId); // Highest relevance chunk ranked first
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task QueryTransformer_GeneratesMultiQueryVariants()
    {
        OllamaQueryTransformationService transformer = new();

        QueryTransformationResult result = await transformer.TransformAsync(
            "Come funziona il retrieval",
            QueryTransformationStrategy.MultiQuery);

        Assert.Equal(QueryTransformationStrategy.MultiQuery, result.Strategy);
        Assert.True(result.ExpandedQueries.Count >= 2);
        Assert.Contains("Come funziona il retrieval", result.ExpandedQueries);
    }

    [Fact]
    public async Task ParentChildResolver_ResolvesParentContext()
    {
        DummyChunkRepo repo = new();
        ParentChildChunkResolver resolver = new(repo);
        SearchChunk childChunk = new(
            ChunkId: 10,
            DocumentId: 1,
            DocumentName: "doc.txt",
            ChunkIndex: 0,
            PageStart: 1,
            PageEnd: 1,
            Content: "Snippet child",
            ParentChunkId: 1,
            ChunkLevel: "Child",
            SectionHeading: "Sezione 1",
            ParentContent: "Paragrafo esteso parent con tutto il contesto del documento.");

        Dictionary<long, SearchChunk> map = new() { [10] = childChunk };
        IReadOnlyDictionary<long, SearchChunk> resolvedMap = await resolver.ResolveAllAsync(map);
        SearchChunk resolved = resolvedMap[10];

        Assert.Equal("Paragrafo esteso parent con tutto il contesto del documento.", resolved.ParentContent);
    }

    [Fact]
    public void CragDecisionEngine_DetectsLowConfidence()
    {
        CragDecisionEngine engine = new();
        List<DocumentSearchResult> lowScoreResults = new()
        {
            new DocumentSearchResult(1, "doc.txt", 1, 1, 10, "irrelevant text", 0.15d, 0.15d)
        };

        CragDecision result = engine.Evaluate(lowScoreResults, "test query", threshold: 0.30d);

        Assert.Equal(CragAction.Reformulate, result.Action);
        Assert.Contains("low", result.SummaryNotice, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DummyChunkRepo : IRetrievalChunkRepository
    {
        public Task<IReadOnlyDictionary<long, SearchChunk>> GetChunksAsync(IReadOnlyCollection<long> chunkIds, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<long, SearchChunk> dict = new Dictionary<long, SearchChunk>();
            return Task.FromResult(dict);
        }
    }
}
