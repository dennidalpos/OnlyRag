using System.IO;
using OnlyRag.Infrastructure.Agent;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public class AgentMctsStateMachineTests
{
    [Fact]
    public void BuildToolCallJsonSchema_ReturnsValidJsonObject()
    {
        var schema = AgentToolJsonSchemaBuilder.BuildToolCallJsonSchema();
        Assert.NotNull(schema);
        Assert.Equal("object", schema["type"]?.ToString());
        Assert.NotNull(schema["properties"]);
        Assert.NotNull(schema["required"]);
    }

    [Fact]
    public void MctsStateMachine_SelectsExpandsAndBackpropagates()
    {
        var checkpointManager = new WorkspaceSnapshotCheckpointManager();
        var stateMachine = new AgentMctsStateMachine(checkpointManager, "Test goal");

        Assert.NotNull(stateMachine.Root);
        Assert.Equal("Goal:Test goal", stateMachine.Root.ActionSignature);

        var expanded = stateMachine.Expand(stateMachine.Root, "read_file:src/Program.cs");
        Assert.NotNull(expanded);
        Assert.Single(stateMachine.Root.Children);

        double reward = AgentMctsStateMachine.EvaluateReward(success: true, hasCompilationError: false, reflectionScore: 0.8);
        Assert.True(reward > 0.5);

        stateMachine.Backpropagate(expanded, reward);
        Assert.Equal(1, expanded.VisitCount);
        Assert.Equal(1, stateMachine.Root.VisitCount);
    }

    [Fact]
    public void MctsStateMachine_PrunesLowReflectionBranches()
    {
        var checkpointManager = new WorkspaceSnapshotCheckpointManager();
        var stateMachine = new AgentMctsStateMachine(checkpointManager, "Test goal");

        var badChild = stateMachine.ExpandAndNavigate("write_file:bad.cs");
        stateMachine.EvaluateAndBackpropagateCurrent(success: true, hasCompilationError: false, reflectionScore: 0.1, minReflectionThreshold: 0.25);

        Assert.True(badChild.IsTerminal);
        Assert.Empty(stateMachine.Root.Children);
    }

    [Fact]
    public void WorkspaceSnapshotCheckpointManager_CapturesAndRestoresFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"onlyrag_mcts_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string filePath = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(filePath, "Original content");

            var manager = new WorkspaceSnapshotCheckpointManager();
            var checkpoint = manager.CreateCheckpoint("cp1", tempDir, new[] { "test.txt" });

            Assert.Equal("Original content", checkpoint.FileSnapshots[filePath]);

            // Modify file content
            File.WriteAllText(filePath, "Mutated content");
            Assert.Equal("Mutated content", File.ReadAllText(filePath));

            // Restore checkpoint
            bool restored = manager.RestoreCheckpoint(checkpoint);
            Assert.True(restored);
            Assert.Equal("Original content", File.ReadAllText(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void MctsStateMachine_SelectsBestCandidateChildAndEvaluatesHeuristics()
    {
        var checkpointManager = new WorkspaceSnapshotCheckpointManager();
        var stateMachine = new AgentMctsStateMachine(checkpointManager, "Test goal");

        double readHeuristic = AgentMctsStateMachine.EvaluateCandidateActionHeuristic("read_file", "{}");
        double planHeuristic = AgentMctsStateMachine.EvaluateCandidateActionHeuristic("plan_task", "{}");
        double writeHeuristic = AgentMctsStateMachine.EvaluateCandidateActionHeuristic("write_file", "{}");

        Assert.True(readHeuristic > writeHeuristic);
        Assert.True(planHeuristic > writeHeuristic);

        var child1 = stateMachine.Expand(stateMachine.Root, "read_file:src/A.cs");
        var child2 = stateMachine.Expand(stateMachine.Root, "write_file:src/B.cs");

        var bestCandidate = stateMachine.SelectBestCandidateChild();
        Assert.NotNull(bestCandidate);
        Assert.Equal(child1, bestCandidate);
    }

    [Fact]
    public void MctsStateMachine_PrunesAndRollsBackActiveBranch()
    {
        var checkpointManager = new WorkspaceSnapshotCheckpointManager();
        var stateMachine = new AgentMctsStateMachine(checkpointManager, "Test goal");

        var active = stateMachine.ExpandAndNavigate("run_command:dotnet build");
        Assert.Equal(active, stateMachine.CurrentActiveNode);

        var parent = stateMachine.PruneAndRollbackActiveBranch();
        Assert.True(active.IsTerminal);
        Assert.Equal(stateMachine.Root, parent);
        Assert.Equal(stateMachine.Root, stateMachine.CurrentActiveNode);
    }
}
