namespace OnlyRag.Infrastructure.Agent.Memory;

public record CachedSubagentReport(
    string Role,
    string PromptHash,
    string WorkspaceRoot,
    string ReportMarkdown,
    IReadOnlyList<string> KeyFacts,
    IReadOnlyList<string> ModifiedFiles,
    DateTimeOffset CreatedAtUtc);

public interface ISubagentReportCacheRepository
{
    Task<CachedSubagentReport?> GetCachedReportAsync(
        string role,
        string prompt,
        string workspaceRoot,
        CancellationToken cancellationToken = default);

    Task SaveCachedReportAsync(
        CachedSubagentReport report,
        CancellationToken cancellationToken = default);
}
