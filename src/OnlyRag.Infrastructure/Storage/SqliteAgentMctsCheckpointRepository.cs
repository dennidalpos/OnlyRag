using System.Globalization;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Storage;

public interface IAgentMctsCheckpointRepository
{
    Task SaveCheckpointAsync(string runId, int stepNumber, string activeNodeId, string treeStateJson, CancellationToken cancellationToken = default);
    Task<AgentMctsCheckpoint?> GetLatestCheckpointAsync(string runId, CancellationToken cancellationToken = default);
    Task DeleteCheckpointsAsync(string runId, CancellationToken cancellationToken = default);
}

public sealed class SqliteAgentMctsCheckpointRepository : IAgentMctsCheckpointRepository
{
    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteAgentMctsCheckpointRepository(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task SaveCheckpointAsync(
        string runId,
        int stepNumber,
        string activeNodeId,
        string treeStateJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(treeStateJson);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_mcts_checkpoints (
                run_id, step_number, active_node_id, tree_state_json, created_at_utc
            ) VALUES (
                $runId, $stepNumber, $activeNodeId, $treeStateJson, $createdAtUtc
            );
            """;

        command.Parameters.AddWithValue("$runId", runId);
        command.Parameters.AddWithValue("$stepNumber", stepNumber);
        command.Parameters.AddWithValue("$activeNodeId", activeNodeId);
        command.Parameters.AddWithValue("$treeStateJson", treeStateJson);
        command.Parameters.AddWithValue("$createdAtUtc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AgentMctsCheckpoint?> GetLatestCheckpointAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, run_id, step_number, active_node_id, tree_state_json, created_at_utc
            FROM agent_mcts_checkpoints
            WHERE run_id = $runId
            ORDER BY step_number DESC, id DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$runId", runId);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new AgentMctsCheckpoint(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture));
    }

    public async Task DeleteCheckpointsAsync(string runId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM agent_mcts_checkpoints WHERE run_id = $runId;";
        command.Parameters.AddWithValue("$runId", runId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
