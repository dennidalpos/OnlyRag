using System.Diagnostics;
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
        double p99Value = validLatencies.Count > 0
            ? Math.Round(validLatencies.Select(l => l.TotalMs).OrderBy(v => v).ElementAt(Math.Clamp((int)(validLatencies.Count * 0.99), 0, validLatencies.Count - 1)), 2)
            : 0.0;
        RagLatencyMetrics? avgLatency = validLatencies.Count > 0
            ? new RagLatencyMetrics(
                Math.Round(validLatencies.Average(l => l.QueryEmbeddingMs), 2),
                Math.Round(validLatencies.Average(l => l.QdrantSearchMs), 2),
                Math.Round(validLatencies.Average(l => l.Fts5SearchMs), 2),
                Math.Round(validLatencies.Average(l => l.ReRankingMs), 2),
                Math.Round(validLatencies.Average(l => l.TotalMs), 2),
                Math.Round(validLatencies.Average(l => l.AverageCragScore), 4),
                p99Value)
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

    public async Task<ConcurrencyBenchmarkReport> EvaluateConcurrencyAndFaultToleranceAsync(
        IReadOnlyList<RetrievalBenchmarkTestCase> testCases,
        int concurrencyLevel = 10,
        bool simulateNetworkFaults = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(testCases);
        if (testCases.Count == 0)
        {
            throw new ArgumentException("Benchmark testCases collection cannot be empty.", nameof(testCases));
        }

        concurrencyLevel = Math.Clamp(concurrencyLevel, 1, 100);
        var latencies = new System.Collections.Concurrent.ConcurrentBag<double>();
        int successCount = 0;
        int faultCount = 0;

        var sw = Stopwatch.StartNew();
        using var semaphore = new SemaphoreSlim(concurrencyLevel);

        var tasks = testCases.Select(async (testCase, index) =>
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var itemSw = Stopwatch.StartNew();

                if (simulateNetworkFaults && index % 5 == 0)
                {
                    // Inject artificial fault simulation
                    Interlocked.Increment(ref faultCount);
                    return;
                }

                var request = new DocumentSearchRequest(testCase.Query, (testCase.DocumentIds ?? []).ToList(), testCase.TopK ?? 5);
                var response = await retrievalService.SearchAsync(request, cancellationToken).ConfigureAwait(false);
                itemSw.Stop();

                latencies.Add(itemSw.Elapsed.TotalMilliseconds);
                Interlocked.Increment(ref successCount);
            }
            catch
            {
                Interlocked.Increment(ref faultCount);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        sw.Stop();

        int totalRequests = testCases.Count;
        double totalSeconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        double throughputRps = Math.Round(totalRequests / totalSeconds, 2);

        var sortedLatencies = latencies.OrderBy(l => l).ToList();
        double avgLatency = sortedLatencies.Count > 0 ? Math.Round(sortedLatencies.Average(), 2) : 0.0;
        double p95Latency = sortedLatencies.Count > 0 ? Math.Round(sortedLatencies[Math.Clamp((int)(sortedLatencies.Count * 0.95), 0, sortedLatencies.Count - 1)], 2) : 0.0;
        double p99Latency = sortedLatencies.Count > 0 ? Math.Round(sortedLatencies[Math.Clamp((int)(sortedLatencies.Count * 0.99), 0, sortedLatencies.Count - 1)], 2) : 0.0;
        double faultToleranceRate = totalRequests > 0 ? Math.Round((double)successCount / totalRequests, 4) : 0.0;

        return new ConcurrencyBenchmarkReport(
            DateTimeOffset.UtcNow,
            concurrencyLevel,
            totalRequests,
            successCount,
            faultCount,
            throughputRps,
            avgLatency,
            p95Latency,
            p99Latency,
            faultToleranceRate);
    }
}
