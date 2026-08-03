using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval;

public record RetrievalBenchmarkTestCase(
    string Id,
    string Query,
    IReadOnlyList<long> ExpectedChunkIds,
    IReadOnlyCollection<long>? DocumentIds = null,
    int? TopK = null);

public record RetrievalBenchmarkCaseResult(
    string Id,
    string Query,
    int TopK,
    IReadOnlyList<long> ExpectedChunkIds,
    IReadOnlyList<long> ReturnedChunkIds,
    IReadOnlyList<long> HitChunkIds,
    double RecallAtK,
    double ReciprocalRank,
    double ApAtK,
    double NdcgAtK,
    int? FirstRelevantRank,
    RagLatencyMetrics? Latency = null);

public record RetrievalBenchmarkReport(
    DateTimeOffset EvaluatedAtUtc,
    int DefaultTopK,
    int CaseCount,
    double AverageRecallAtK,
    double Mrr,
    double MapAtK,
    double NdcgAtK,
    IReadOnlyList<RetrievalBenchmarkCaseResult> Cases,
    RagLatencyMetrics? AverageLatency = null);

public interface IRetrievalBenchmarkReportService
{
    Task<RetrievalBenchmarkReport> EvaluateBenchmarkAsync(
        IReadOnlyList<RetrievalBenchmarkTestCase> testCases,
        int defaultTopK = 5,
        CancellationToken cancellationToken = default);
}
