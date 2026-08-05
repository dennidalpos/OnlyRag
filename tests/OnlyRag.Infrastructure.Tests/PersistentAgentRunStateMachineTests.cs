using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;

namespace OnlyRag.Infrastructure.Tests;

public sealed class PersistentAgentRunStateMachineTests
{
    [Fact]
    public void StateMachine_RequiresObservationAndVerificationBeforeNextPlan()
    {
        PersistentAgentRunStateMachine machine = CreateMachine();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        machine.TransitionTo(AgentRunPhase.Act, "Action selected.", now);
        machine.TransitionTo(AgentRunPhase.Observe, "Action completed.", now);
        machine.TransitionTo(AgentRunPhase.Verify, "Observation available.", now);
        machine.TransitionTo(AgentRunPhase.Plan, "Verification completed.", now);

        Assert.Equal(AgentRunPhase.Plan, machine.Snapshot.Phase);
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(AgentRunPhase.Completed, "Cannot skip finalization.", now));
    }

    [Fact]
    public void StateMachine_BlocksFinalizeUntilEveryRequiredCriterionHasPassed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentCompletionCriterion build = new("build", "Build the app.", AgentCompletionVerificationKind.Command, "run_command", "dotnet build");
        AgentCompletionCriterion tests = new("tests", "Run tests.", AgentCompletionVerificationKind.Command, "run_command", "dotnet test");
        AgentRunSnapshot snapshot = new(
            "run", "goal", "write", null, "workspace", AgentRunPhase.Plan, new AgentRunBudget(),
            0, 0, now, now, null, null, [], [build, tests]);
        PersistentAgentRunStateMachine machine = new(snapshot);

        machine.TransitionTo(AgentRunPhase.Act, "Action selected.", now);
        Assert.Throws<InvalidOperationException>(() => machine.TransitionTo(AgentRunPhase.Finalize, "No evidence.", now));

        AgentToolCall buildCall = new("build-call", "run_command", "{\"commandLine\":\"dotnet build\"}");
        machine.RecordVerification(buildCall, new AgentToolResult("build-call", "run_command", true, "Build succeeded."), now);
        Assert.False(machine.CanFinalize());

        AgentToolCall testCall = new("test-call", "run_command", "{\"commandLine\":\"dotnet test\"}");
        machine.RecordVerification(testCall, new AgentToolResult("test-call", "run_command", true, "Tests passed."), now);
        Assert.True(machine.CanFinalize());
        machine.TransitionTo(AgentRunPhase.Observe, "Tool observations recorded.", now);
        machine.TransitionTo(AgentRunPhase.Verify, "Verification evidence recorded.", now);
        machine.TransitionTo(AgentRunPhase.Finalize, "Criteria passed.", now);
        machine.TransitionTo(AgentRunPhase.Completed, "Final response recorded.", now);
    }

    [Fact]
    public void StateMachine_FailedVerificationDoesNotSatisfyCriterion()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentCompletionCriterion tests = new("tests", "Run tests.", AgentCompletionVerificationKind.Command, "run_command", "dotnet test");
        AgentRunSnapshot snapshot = new(
            "run", "goal", "write", null, "workspace", AgentRunPhase.Plan, new AgentRunBudget(),
            0, 0, now, now, null, null, [], [tests]);
        PersistentAgentRunStateMachine machine = new(snapshot);
        machine.RecordVerification(
            new AgentToolCall("test-call", "run_command", "{\"commandLine\":\"dotnet test\"}"),
            new AgentToolResult("test-call", "run_command", false, string.Empty, "Tests failed."), now);

        Assert.False(machine.CanFinalize());
        Assert.Equal(AgentCompletionVerificationStatus.Failed, Assert.Single(machine.Snapshot.EffectiveCompletionVerifications).Status);
    }

    [Fact]
    public void StateMachine_EnforcesToolTokenAndDurationBudgets()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentRunSnapshot snapshot = new(
            "run", "goal", "write", null, "workspace", AgentRunPhase.Plan,
            new AgentRunBudget(MaxToolCalls: 1, MaxEstimatedTokens: 4, MaxDuration: TimeSpan.FromSeconds(1)),
            0, 0, now, now, null, null, []);
        PersistentAgentRunStateMachine machine = new(snapshot);

        machine.ConsumeToolCall(now);
        Assert.Throws<AgentRunBudgetExceededException>(() => machine.ConsumeToolCall(now));
        machine.ConsumeEstimatedTokens(4, now);
        Assert.Throws<AgentRunBudgetExceededException>(() => machine.ConsumeEstimatedTokens(1, now));
        Assert.Throws<AgentRunBudgetExceededException>(() => machine.EnsureWithinTimeBudget(now.AddSeconds(2)));
    }

    private static PersistentAgentRunStateMachine CreateMachine()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AgentRunSnapshot snapshot = new(
            "run", "goal", "write", null, "workspace", AgentRunPhase.Plan, new AgentRunBudget(),
            0, 0, now, now, null, null, []);
        return new PersistentAgentRunStateMachine(snapshot);
    }
}
