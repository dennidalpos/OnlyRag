using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqlitePolicyAuditRepository(ISqliteConnectionFactory connectionFactory)
{
    public async Task LogRecordAsync(AuditLogRecord record, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_policy_audit_logs (
                call_id, tool_name, risk_level, allowed, workspace_root, arguments_json, output_or_error, timestamp_utc
            ) VALUES (
                $callId, $toolName, $riskLevel, $allowed, $workspaceRoot, $argumentsJson, $outputOrError, $timestampUtc
            );
            """;
        command.Parameters.AddWithValue("$callId", record.CallId);
        command.Parameters.AddWithValue("$toolName", record.ToolName);
        command.Parameters.AddWithValue("$riskLevel", record.RiskLevel);
        command.Parameters.AddWithValue("$allowed", record.Allowed ? 1 : 0);
        command.Parameters.AddWithValue("$workspaceRoot", record.WorkspaceRoot ?? string.Empty);
        command.Parameters.AddWithValue("$argumentsJson", record.ArgumentsJson ?? "{}");
        command.Parameters.AddWithValue("$outputOrError", (object?)record.OutputOrError ?? DBNull.Value);
        command.Parameters.AddWithValue("$timestampUtc", record.TimestampUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditLogRecord>> GetAuditLogsAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, call_id, tool_name, risk_level, allowed, workspace_root, arguments_json, output_or_error, timestamp_utc
            FROM agent_policy_audit_logs
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<AuditLogRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AuditLogRecord(
                Id: reader.GetInt64(0),
                CallId: reader.GetString(1),
                ToolName: reader.GetString(2),
                RiskLevel: reader.GetString(3),
                Allowed: reader.GetInt32(4) != 0,
                WorkspaceRoot: reader.GetString(5),
                ArgumentsJson: reader.GetString(6),
                OutputOrError: reader.IsDBNull(7) ? null : reader.GetString(7),
                TimestampUtc: DateTimeOffset.Parse(reader.GetString(8))
            ));
        }

        return results;
    }
}
