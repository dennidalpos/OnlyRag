using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class RetrievalBenchmarkReportServiceTests
{
    private class MockHybridRetrievalService : IHybridRetrievalService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = new DocumentSearchResult[]
            {
                new(1, "doc1", 1, 1, 101, "First chunk content", 0.95),
                new(1, "doc1", 1, 1, 102, "Second chunk content", 0.85),
                new(1, "doc1", 1, 1, 103, "Third chunk content", 0.75)
            };
            return Task.FromResult(new DocumentSearchResponse(results, [], "FTS5", "Qdrant", 4000));
        }
    }

    [Fact]
    public async Task EvaluateBenchmarkAsync_CalculatesMetricsCorrectly()
    {
        var mockService = new MockHybridRetrievalService();
        var benchmarkService = new RetrievalBenchmarkReportService(mockService);

        var testCases = new List<RetrievalBenchmarkTestCase>
        {
            new("tc_1", "What is RAG?", new long[] { 101, 103 }, null, 5)
        };

        var report = await benchmarkService.EvaluateBenchmarkAsync(testCases, defaultTopK: 5);

        Assert.NotNull(report);
        Assert.Equal(1, report.CaseCount);
        Assert.Equal(1.0, report.AverageRecallAtK);
        Assert.Equal(1.0, report.Mrr);
        Assert.True(report.NdcgAtK > 0.0);
        Assert.Single(report.Cases);
        Assert.Equal("tc_1", report.Cases[0].Id);
        Assert.Equal(1, report.Cases[0].FirstRelevantRank);
    }

    [Fact]
    public async Task EvaluateConcurrencyAndFaultToleranceAsync_RunsConcurrentWorkload()
    {
        var mockService = new MockHybridRetrievalService();
        var benchmarkService = new RetrievalBenchmarkReportService(mockService);

        var testCases = Enumerable.Range(1, 20)
            .Select(i => new RetrievalBenchmarkTestCase($"tc_{i}", $"Query {i}", new long[] { 101 }, null, 5))
            .ToList();

        var report = await benchmarkService.EvaluateConcurrencyAndFaultToleranceAsync(testCases, concurrencyLevel: 5, simulateNetworkFaults: true);

        Assert.NotNull(report);
        Assert.Equal(20, report.TotalRequests);
        Assert.Equal(5, report.ConcurrentClients);
        Assert.True(report.ThroughputRps >= 0);
        Assert.True(report.FaultedRequests > 0);
        Assert.True(report.SuccessfulRequests > 0);
    }
}
