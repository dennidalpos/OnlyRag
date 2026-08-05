using System.Text.Json;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public sealed class SqliteAgentRunStateRepository(ISqliteConnectionFactory connectionFactory) : IAgentRunStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AgentRunSnapshot> CreateAsync(AgentRunSnapshot run, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_runs (
                run_id, goal, mode, model, workspace_root, phase, budget_json, tool_calls_used,
                estimated_tokens_used, started_at_utc, updated_at_utc, last_error, final_response, messages_json,
                completion_criteria_json, completion_verifications_json)
            VALUES (
                $runId, $goal, $mode, $model, $workspaceRoot, $phase, $budgetJson, $toolCallsUsed,
                $estimatedTokensUsed, $startedAtUtc, $updatedAtUtc, $lastError, $finalResponse, $messagesJson,
                $completionCriteriaJson, $completionVerificationsJson);
            """;
        Bind(command, run);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return run;
    }

    public async Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM agent_runs WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task SaveAsync(AgentRunSnapshot run, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE agent_runs SET
                phase = $phase, budget_json = $budgetJson, tool_calls_used = $toolCallsUsed,
                estimated_tokens_used = $estimatedTokensUsed, updated_at_utc = $updatedAtUtc,
                last_error = $lastError, final_response = $finalResponse, messages_json = $messagesJson,
                completion_criteria_json = $completionCriteriaJson,
                completion_verifications_json = $completionVerificationsJson
            WHERE run_id = $runId;
            """;
        Bind(command, run);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"Agent run '{run.RunId}' was not found.");
        }
    }

    public async Task AppendTransitionAsync(AgentRunTransition transition, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_run_transitions (run_id, from_phase, to_phase, reason, occurred_at_utc)
            VALUES ($runId, $fromPhase, $toPhase, $reason, $occurredAtUtc);
            """;
        command.Parameters.AddWithValue("$runId", transition.RunId);
        command.Parameters.AddWithValue("$fromPhase", transition.From.ToString());
        command.Parameters.AddWithValue("$toPhase", transition.To.ToString());
        command.Parameters.AddWithValue("$reason", transition.Reason);
        command.Parameters.AddWithValue("$occurredAtUtc", transition.OccurredAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendTraceEventAsync(AgentRunTraceEvent traceEvent, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_run_trace_events (run_id, step, event_type, occurred_at_utc, phase, decision, tool_name, tool_call_id, success, observation, error, estimated_tokens, tool_calls_used, latency_ms, evidence, outcome)
            VALUES ($runId, $step, $eventType, $occurredAtUtc, $phase, $decision, $toolName, $toolCallId, $success, $observation, $error, $estimatedTokens, $toolCallsUsed, $latencyMs, $evidence, $outcome);
            """;
        BindTrace(command, traceEvent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentRunTraceEvent>> ListTraceEventsAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM agent_run_trace_events WHERE run_id = $runId ORDER BY id;";
        command.Parameters.AddWithValue("$runId", runId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<AgentRunTraceEvent> events = [];
        while (await reader.ReadAsync(cancellationToken)) events.Add(ReadTrace(reader));
        return events;
    }

    public async Task<IReadOnlyList<AgentRunSnapshot>> GetResumableAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM agent_runs
            WHERE phase NOT IN ('Completed', 'Failed', 'Cancelled')
            ORDER BY updated_at_utc DESC;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        List<AgentRunSnapshot> runs = [];
        while (await reader.ReadAsync(cancellationToken)) runs.Add(Read(reader));
        return runs;
    }

    private static void Bind(SqliteCommand command, AgentRunSnapshot run)
    {
        command.Parameters.AddWithValue("$runId", run.RunId);
        command.Parameters.AddWithValue("$goal", run.Goal);
        command.Parameters.AddWithValue("$mode", run.Mode);
        command.Parameters.AddWithValue("$model", (object?)run.Model ?? DBNull.Value);
        command.Parameters.AddWithValue("$workspaceRoot", run.WorkspaceRoot);
        command.Parameters.AddWithValue("$phase", run.Phase.ToString());
        command.Parameters.AddWithValue("$budgetJson", JsonSerializer.Serialize(run.Budget, JsonOptions));
        command.Parameters.AddWithValue("$toolCallsUsed", run.ToolCallsUsed);
        command.Parameters.AddWithValue("$estimatedTokensUsed", run.EstimatedTokensUsed);
        command.Parameters.AddWithValue("$startedAtUtc", run.StartedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", run.UpdatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$lastError", (object?)run.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$finalResponse", (object?)run.FinalResponse ?? DBNull.Value);
        command.Parameters.AddWithValue("$messagesJson", JsonSerializer.Serialize(run.Messages, JsonOptions));
        command.Parameters.AddWithValue("$completionCriteriaJson", JsonSerializer.Serialize(run.EffectiveCompletionCriteria, JsonOptions));
        command.Parameters.AddWithValue("$completionVerificationsJson", JsonSerializer.Serialize(run.EffectiveCompletionVerifications, JsonOptions));
    }

    private static AgentRunSnapshot Read(SqliteDataReader reader)
    {
        AgentRunBudget budget = JsonSerializer.Deserialize<AgentRunBudget>(reader.GetString(reader.GetOrdinal("budget_json")), JsonOptions)
            ?? new AgentRunBudget();
        IReadOnlyList<string> messages = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("messages_json")), JsonOptions)
            ?? [];
        IReadOnlyList<AgentCompletionCriterion> criteria = JsonSerializer.Deserialize<List<AgentCompletionCriterion>>(reader.GetString(reader.GetOrdinal("completion_criteria_json")), JsonOptions)
            ?? [];
        IReadOnlyList<AgentCompletionVerification> verifications = JsonSerializer.Deserialize<List<AgentCompletionVerification>>(reader.GetString(reader.GetOrdinal("completion_verifications_json")), JsonOptions)
            ?? [];
        return new AgentRunSnapshot(
            reader.GetString(reader.GetOrdinal("run_id")),
            reader.GetString(reader.GetOrdinal("goal")),
            reader.GetString(reader.GetOrdinal("mode")),
            reader.IsDBNull(reader.GetOrdinal("model")) ? null : reader.GetString(reader.GetOrdinal("model")),
            reader.GetString(reader.GetOrdinal("workspace_root")),
            Enum.Parse<AgentRunPhase>(reader.GetString(reader.GetOrdinal("phase")), ignoreCase: false),
            budget,
            reader.GetInt32(reader.GetOrdinal("tool_calls_used")),
            reader.GetInt32(reader.GetOrdinal("estimated_tokens_used")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at_utc"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at_utc"))),
            reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
            reader.IsDBNull(reader.GetOrdinal("final_response")) ? null : reader.GetString(reader.GetOrdinal("final_response")),
            messages,
            criteria,
            verifications);
    }

    private static void BindTrace(SqliteCommand command, AgentRunTraceEvent traceEvent)
    {
        command.Parameters.AddWithValue("$runId", traceEvent.RunId);
        command.Parameters.AddWithValue("$step", traceEvent.Step);
        command.Parameters.AddWithValue("$eventType", traceEvent.EventType);
        command.Parameters.AddWithValue("$occurredAtUtc", traceEvent.OccurredAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$phase", traceEvent.Phase.ToString());
        command.Parameters.AddWithValue("$decision", (object?)traceEvent.Decision ?? DBNull.Value);
        command.Parameters.AddWithValue("$toolName", (object?)traceEvent.ToolName ?? DBNull.Value);
        command.Parameters.AddWithValue("$toolCallId", (object?)traceEvent.ToolCallId ?? DBNull.Value);
        command.Parameters.AddWithValue("$success", traceEvent.Success is null ? DBNull.Value : traceEvent.Success.Value ? 1 : 0);
        command.Parameters.AddWithValue("$observation", (object?)traceEvent.Observation ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)traceEvent.Error ?? DBNull.Value);
        command.Parameters.AddWithValue("$estimatedTokens", (object?)traceEvent.EstimatedTokens ?? DBNull.Value);
        command.Parameters.AddWithValue("$toolCallsUsed", (object?)traceEvent.ToolCallsUsed ?? DBNull.Value);
        command.Parameters.AddWithValue("$latencyMs", (object?)traceEvent.LatencyMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$evidence", (object?)traceEvent.Evidence ?? DBNull.Value);
        command.Parameters.AddWithValue("$outcome", (object?)traceEvent.Outcome ?? DBNull.Value);
    }

    private static AgentRunTraceEvent ReadTrace(SqliteDataReader reader) => new(
        reader.GetInt64(reader.GetOrdinal("id")), reader.GetString(reader.GetOrdinal("run_id")), reader.GetInt32(reader.GetOrdinal("step")),
        reader.GetString(reader.GetOrdinal("event_type")), DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("occurred_at_utc"))),
        Enum.Parse<AgentRunPhase>(reader.GetString(reader.GetOrdinal("phase"))),
        ReadNullableString(reader, "decision"), ReadNullableString(reader, "tool_name"), ReadNullableString(reader, "tool_call_id"),
        reader.IsDBNull(reader.GetOrdinal("success")) ? null : reader.GetInt64(reader.GetOrdinal("success")) != 0,
        ReadNullableString(reader, "observation"), ReadNullableString(reader, "error"),
        reader.IsDBNull(reader.GetOrdinal("estimated_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("estimated_tokens")),
        reader.IsDBNull(reader.GetOrdinal("tool_calls_used")) ? null : reader.GetInt32(reader.GetOrdinal("tool_calls_used")),
        reader.IsDBNull(reader.GetOrdinal("latency_ms")) ? null : reader.GetDouble(reader.GetOrdinal("latency_ms")),
        ReadNullableString(reader, "evidence"), ReadNullableString(reader, "outcome"));

    private static string? ReadNullableString(SqliteDataReader reader, string column) => reader.IsDBNull(reader.GetOrdinal(column)) ? null : reader.GetString(reader.GetOrdinal(column));
}
