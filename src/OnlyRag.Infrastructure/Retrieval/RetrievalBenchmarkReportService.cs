using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class RetrievalBenchmarkReportService : IRetrievalBenchmarkReportService
{
    private readonly IHybridRetrievalService retrievalService;

    public RetrievalBenchmarkReportService(IHybridRetrievalService retrievalService)
    {
        this.retrievalService = retrievalService;
    }

    public async Task<RetrievalBenchmarkReport> EvaluateBenchmarkAsync(
        IReadOnlyList<RetrievalBenchmarkTestCase> testCases,
        int defaultTopK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCases);
        if (testCases.Count == 0)
        {
            throw new ArgumentException("Benchmark testCases collection cannot be empty.", nameof(testCases));
        }

        var results = new List<RetrievalBenchmarkCaseResult>();

        foreach (var testCase in testCases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int topK = testCase.TopK ?? defaultTopK;

            var request = new DocumentSearchRequest(
                testCase.Query,
                (testCase.DocumentIds ?? []).ToList(),
                topK);

            var searchResponse = await retrievalService.SearchAsync(
                request,
                cancellationToken);

            var returnedChunkIds = searchResponse.Results.Select(r => r.ChunkId).Take(topK).ToList();
            var expectedChunkIds = testCase.ExpectedChunkIds;

            var hitChunkIds = expectedChunkIds.Where(id => returnedChunkIds.Contains(id)).ToList();

            int? firstRelevantRank = null;
            for (int i = 0; i < returnedChunkIds.Count; i++)
            {
                if (expectedChunkIds.Contains(returnedChunkIds[i]))
                {
                    firstRelevantRank = i + 1;
                    break;
                }
            }

            double recallAtK = expectedChunkIds.Count > 0 ? (double)hitChunkIds.Count / expectedChunkIds.Count : 0.0;
            double reciprocalRank = firstRelevantRank.HasValue ? 1.0 / firstRelevantRank.Value : 0.0;

            double dcgAtK = 0.0;
            int hitsCount = 0;
            double sumPrecision = 0.0;

            for (int i = 0; i < returnedChunkIds.Count; i++)
            {
                int rank = i + 1;
                if (expectedChunkIds.Contains(returnedChunkIds[i]))
                {
                    hitsCount++;
                    sumPrecision += (double)hitsCount / rank;
                    dcgAtK += 1.0 / Math.Log2(rank + 1);
                }
            }

            int maxPossibleHits = Math.Min(topK, expectedChunkIds.Count);
            double apAtK = maxPossibleHits > 0 ? sumPrecision / maxPossibleHits : 0.0;

            double idcgAtK = 0.0;
            for (int rank = 1; rank <= maxPossibleHits; rank++)
            {
                idcgAtK += 1.0 / Math.Log2(rank + 1);
            }

            double ndcgAtK = idcgAtK > 0.0 ? dcgAtK / idcgAtK : 0.0;

            results.Add(new RetrievalBenchmarkCaseResult(
                testCase.Id,
                testCase.Query,
                topK,
                expectedChunkIds,
                returnedChunkIds,
                hitChunkIds,
                Math.Round(recallAtK, 4),
                Math.Round(reciprocalRank, 4),
                Math.Round(apAtK, 4),
                Math.Round(ndcgAtK, 4),
                firstRelevantRank,
                searchResponse.LatencyMetrics));
        }

        double avgRecall = results.Average(r => r.RecallAtK);
        double mrr = results.Average(r => r.ReciprocalRank);
        double mapAtK = results.Average(r => r.ApAtK);
        double avgNdcg = results.Average(r => r.NdcgAtK);

        var validLatencies = results.Select(r => r.Latency).Where(l => l is not null).Cast<RagLatencyMetrics>().ToList();
        RagLatencyMetrics? avgLatency = validLatencies.Count > 0
            ? new RagLatencyMetrics(
                Math.Round(validLatencies.Average(l => l.QueryEmbeddingMs), 2),
                Math.Round(validLatencies.Average(l => l.QdrantSearchMs), 2),
                Math.Round(validLatencies.Average(l => l.Fts5SearchMs), 2),
                Math.Round(validLatencies.Average(l => l.ReRankingMs), 2),
                Math.Round(validLatencies.Average(l => l.TotalMs), 2),
                Math.Round(validLatencies.Average(l => l.AverageCragScore), 4))
            : null;

        return new RetrievalBenchmarkReport(
            DateTimeOffset.UtcNow,
            defaultTopK,
            results.Count,
            Math.Round(avgRecall, 4),
            Math.Round(mrr, 4),
            Math.Round(mapAtK, 4),
            Math.Round(avgNdcg, 4),
            results,
            avgLatency);
    }
}
