using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent.Memory;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class SubagentReportCacheRepositoryTests : IDisposable
{
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly LocalSqliteSchemaInitializer schemaInitializer;

    public SubagentReportCacheRepositoryTests()
    {
        string testRootDir = Path.Combine(Path.GetTempPath(), $"onlyrag_subcache_test_{Guid.NewGuid():N}");
        var paths = AppStoragePaths.FromRoot(testRootDir);
        descriptor = new LocalSqliteStoreDescriptor(paths);
        connectionFactory = new LocalSqliteConnectionFactory(descriptor);
        schemaInitializer = new LocalSqliteSchemaInitializer(descriptor, connectionFactory);
        schemaInitializer.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(descriptor.Paths.DataRoot)) Directory.Delete(descriptor.Paths.DataRoot, true);
        }
        catch { }
    }

    [Fact]
    public async Task SqliteSubagentReportCacheRepository_Saves_And_Retrieves_Report()
    {
        var repo = new SqliteSubagentReportCacheRepository(connectionFactory);

        string role = "researcher";
        string prompt = "Analyze Qdrant indexing";
        string workspaceRoot = @"C:\TestWorkspace";

        var cachedBefore = await repo.GetCachedReportAsync(role, prompt, workspaceRoot);
        Assert.Null(cachedBefore);

        string promptHash = SqliteSubagentReportCacheRepository.ComputeHash($"{role.Trim().ToLowerInvariant()}:{prompt.Trim()}:{workspaceRoot.Trim().ToLowerInvariant()}");
        var report = new CachedSubagentReport(
            role,
            promptHash,
            workspaceRoot,
            "### [SUBAGENT OUTPUT: researcher]\n\nAnalysis complete.",
            new[] { "KeyFact1" },
            new[] { "ModifiedFile1" },
            DateTimeOffset.UtcNow);

        await repo.SaveCachedReportAsync(report);

        var cachedAfter = await repo.GetCachedReportAsync(role, prompt, workspaceRoot);
        Assert.NotNull(cachedAfter);
        Assert.Equal(role, cachedAfter.Role);
        Assert.Contains("Analysis complete", cachedAfter.ReportMarkdown);
        Assert.Single(cachedAfter.KeyFacts);
        Assert.Equal("KeyFact1", cachedAfter.KeyFacts[0]);
    }
}
