using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Agent.Memory;

public sealed class SqliteSubagentReportCacheRepository : ISubagentReportCacheRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteSubagentReportCacheRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task<CachedSubagentReport?> GetCachedReportAsync(
        string role,
        string prompt,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        string promptHash = ComputeHash($"{role.Trim().ToLowerInvariant()}:{prompt.Trim()}:{workspaceRoot.Trim().ToLowerInvariant()}");
        string cacheKey = $"{role.Trim().ToLowerInvariant()}_{promptHash[..Math.Min(16, promptHash.Length)]}";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT role, prompt_hash, workspace_root, report_markdown, key_facts_json, modified_files_json, created_at_utc
            FROM subagent_report_cache
            WHERE cache_key = $cache_key;";
        command.Parameters.AddWithValue("$cache_key", cacheKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        string fetchedRole = reader.GetString(0);
        string fetchedHash = reader.GetString(1);
        string fetchedWs = reader.GetString(2);
        string reportMarkdown = reader.GetString(3);
        string keyFactsJson = reader.GetString(4);
        string modifiedFilesJson = reader.GetString(5);
        DateTimeOffset createdAt = DateTimeOffset.Parse(reader.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind);

        var keyFacts = JsonSerializer.Deserialize<List<string>>(keyFactsJson) ?? [];
        var modifiedFiles = JsonSerializer.Deserialize<List<string>>(modifiedFilesJson) ?? [];

        return new CachedSubagentReport(fetchedRole, fetchedHash, fetchedWs, reportMarkdown, keyFacts, modifiedFiles, createdAt);
    }

    public async Task SaveCachedReportAsync(
        CachedSubagentReport report,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"{report.Role.Trim().ToLowerInvariant()}_{report.PromptHash[..Math.Min(16, report.PromptHash.Length)]}";

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO subagent_report_cache (
                cache_key, role, prompt_hash, workspace_root, report_markdown, key_facts_json, modified_files_json, created_at_utc
            ) VALUES (
                $cache_key, $role, $prompt_hash, $workspace_root, $report_markdown, $key_facts_json, $modified_files_json, $created_at_utc
            ) ON CONFLICT(cache_key) DO UPDATE SET
                report_markdown = excluded.report_markdown,
                key_facts_json = excluded.key_facts_json,
                modified_files_json = excluded.modified_files_json,
                created_at_utc = excluded.created_at_utc;";

        command.Parameters.AddWithValue("$cache_key", cacheKey);
        command.Parameters.AddWithValue("$role", report.Role);
        command.Parameters.AddWithValue("$prompt_hash", report.PromptHash);
        command.Parameters.AddWithValue("$workspace_root", report.WorkspaceRoot);
        command.Parameters.AddWithValue("$report_markdown", report.ReportMarkdown);
        command.Parameters.AddWithValue("$key_facts_json", JsonSerializer.Serialize(report.KeyFacts));
        command.Parameters.AddWithValue("$modified_files_json", JsonSerializer.Serialize(report.ModifiedFiles));
        command.Parameters.AddWithValue("$created_at_utc", report.CreatedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string ComputeHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
