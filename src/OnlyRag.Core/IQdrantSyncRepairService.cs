namespace OnlyRag.Core;

public sealed record QdrantSyncReport(
    int TotalDocumentsChecked,
    int TotalChunksInStorage,
    int MissingVectorCount,
    int EnqueuedRepairJobs,
    bool RecreatedCollections,
    IReadOnlyList<string> Notices);

public interface IQdrantSyncRepairService
{
    Task<QdrantSyncReport> AuditAndRepairAsync(CancellationToken cancellationToken = default);
}
