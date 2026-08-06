using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed partial class DocumentIngestionServiceTests
{
    [Fact]
    public async Task MctsStateMachine_SnapshotSerializationAndSqlitePersistence_RestoresAccurately()
    {
        using TempStorage tempStorage = await TempStorage.CreateInitializedAsync();
        var checkpointManager = new WorkspaceSnapshotCheckpointManager();
        var mcts = new AgentMctsStateMachine(checkpointManager, "Test complex goal");

        var child1 = mcts.ExpandAndNavigate("tool:write_to_file:test.cs");
        mcts.EvaluateAndBackpropagateCurrent(success: true, hasCompilationError: false, reflectionScore: 0.9);

        var child2 = mcts.ExpandAndNavigate("tool:run_command:dotnet build");
        mcts.EvaluateAndBackpropagateCurrent(success: true, hasCompilationError: false, reflectionScore: 0.95);

        string snapshotJson = mcts.ToSnapshotJson();
        Assert.False(string.IsNullOrWhiteSpace(snapshotJson));

        var runRepo = new SqliteAgentRunStateRepository(tempStorage.ConnectionFactory);
        string runId = $"run_{Guid.NewGuid():N}";
        var runSnapshot = new AgentRunSnapshot(
            runId,
            "Test complex goal",
            "write",
            "test-model",
            "workspace-root",
            AgentRunPhase.Plan,
            new AgentRunBudget(),
            0,
            0,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            [],
            [],
            []);
        await runRepo.CreateAsync(runSnapshot);

        var repo = new SqliteAgentMctsCheckpointRepository(tempStorage.ConnectionFactory);
        await repo.SaveCheckpointAsync(runId, stepNumber: 1, mcts.CurrentActiveNode.NodeId, snapshotJson);

        var loadedCheckpoint = await repo.GetLatestCheckpointAsync(runId);
        Assert.NotNull(loadedCheckpoint);
        Assert.Equal(1, loadedCheckpoint.StepNumber);
        Assert.Equal(mcts.CurrentActiveNode.NodeId, loadedCheckpoint.ActiveNodeId);

        var restoredMcts = AgentMctsStateMachine.FromSnapshotJson(checkpointManager, loadedCheckpoint.TreeStateJson);
        Assert.Equal(mcts.CurrentActiveNode.NodeId, restoredMcts.CurrentActiveNode.NodeId);
        Assert.Equal("tool:run_command:dotnet build", restoredMcts.CurrentActiveNode.ActionSignature);
        Assert.Equal(0.95, restoredMcts.CurrentActiveNode.ReflectionScore);
    }
}
