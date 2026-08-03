namespace OnlyRag.Core;

public sealed record SqliteDatabaseStatusResponse(
    string DatabasePath,
    bool Exists,
    long FileSizeBytes,
    string FormattedFileSize,
    bool Fts5Enabled,
    DateTimeOffset? LastMaintenanceAtUtc,
    string MaintenanceStatus);

public sealed record SqliteMaintenanceResultResponse(
    bool Success,
    long InitialFileSizeBytes,
    long FinalFileSizeBytes,
    long BytesReclaimed,
    TimeSpan Duration,
    string Message,
    DateTimeOffset ExecutedAtUtc);

public interface ISqliteMaintenanceService
{
    Task<SqliteDatabaseStatusResponse> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<SqliteMaintenanceResultResponse> RunMaintenanceAsync(CancellationToken cancellationToken = default);
}
