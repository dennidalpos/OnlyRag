using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class AgentSecurityPolicyTests
{
    private static (AgentExecutionPolicyService service, SqlitePolicyAuditRepository repo, string dbPath) CreateTestService()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "OnlyRagPolicyTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        AppStoragePaths paths = AppStoragePaths.FromRoot(tempDir);
        var descriptor = new LocalSqliteStoreDescriptor(paths);

        var factory = new LocalSqliteConnectionFactory(descriptor);
        var initializer = new LocalSqliteSchemaInitializer(descriptor, factory);
        initializer.InitializeAsync().GetAwaiter().GetResult();

        var repo = new SqlitePolicyAuditRepository(factory);
        var service = new AgentExecutionPolicyService(repo);
        return (service, repo, tempDir);
    }

    [Fact]
    public async Task EvaluateAsync_ReadOnlyTool_AllowsExecution()
    {
        var (service, repo, tempDir) = CreateTestService();
        try
        {
            ToolExecutionContext context = new("call_1", "read_file", "{\"filePath\": \"test.txt\"}", tempDir);
            AgentPolicyDecision decision = await service.EvaluateAsync(context);

            Assert.True(decision.Allowed);
            Assert.Equal(AgentRiskLevel.Low, decision.RiskLevel);

            var logs = await repo.GetAuditLogsAsync();
            Assert.Single(logs);
            Assert.Equal("read_file", logs[0].ToolName);
            Assert.True(logs[0].Allowed);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_PathOutsideSandbox_BlocksExecution()
    {
        var (service, repo, tempDir) = CreateTestService();
        try
        {
            string outsidePath = Path.GetFullPath(Path.Combine(tempDir, "..", "secret.txt"));
            string args = JsonSerializer.Serialize(new { filePath = outsidePath });

            ToolExecutionContext context = new("call_2", "write_file", args, tempDir);
            AgentPolicyDecision decision = await service.EvaluateAsync(context);

            Assert.False(decision.Allowed);
            Assert.Contains("outside the authorized workspace sandbox", decision.DenialReason);

            var logs = await repo.GetAuditLogsAsync();
            Assert.Single(logs);
            Assert.False(logs[0].Allowed);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_BlacklistedCommand_BlocksExecution()
    {
        var (service, repo, tempDir) = CreateTestService();
        try
        {
            string args = JsonSerializer.Serialize(new { commandLine = "format C: /q" });
            ToolExecutionContext context = new("call_3", "run_command", args, tempDir);
            AgentPolicyDecision decision = await service.EvaluateAsync(context);

            Assert.False(decision.Allowed);
            Assert.Equal(AgentRiskLevel.Critical, decision.RiskLevel);
            Assert.Contains("forbidden execution token", decision.DenialReason);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task EvaluateAsync_PathWithSharedPrefix_BlocksExecution()
    {
        var (service, repo, tempDir) = CreateTestService();
        try
        {
            string siblingPath = Path.GetFullPath(tempDir + "2") + Path.DirectorySeparatorChar + "secret.txt";
            string args = JsonSerializer.Serialize(new { filePath = siblingPath });

            ToolExecutionContext context = new("call_4", "write_file", args, tempDir);
            AgentPolicyDecision decision = await service.EvaluateAsync(context);

            Assert.False(decision.Allowed);
            Assert.Contains("outside the authorized workspace sandbox", decision.DenialReason);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostExecutionVerify_LogsResultToAudit()
    {
        var (service, repo, tempDir) = CreateTestService();
        try
        {
            ToolExecutionContext context = new("call_4", "list_dir", "{}", tempDir);
            await service.PostExecutionVerifyAsync(context, true, "dir content", null);

            var logs = await repo.GetAuditLogsAsync();
            Assert.Single(logs);
            Assert.Contains("[SUCCESS]", logs[0].OutputOrError);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
