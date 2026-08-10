using OnlyRag.Api;
using OnlyRag.Core;
using Xunit;

namespace OnlyRag.Api.Tests;

public sealed class AgentModePolicyTests
{
    [Theory]
    [InlineData(null, "FULL")]
    [InlineData("write", "FULL")]
    [InlineData("full", "FULL")]
    [InlineData("plan", "PLAN")]
    [InlineData("ask", "ASK")]
    public void NormalizeMode_MapsSupportedModes(string? mode, string expected)
    {
        Assert.Equal(expected, AgentLoopEngine.NormalizeMode(mode));
    }

    [Fact]
    public void AskMode_AllowsReadOnlyToolsButDeniesMutations()
    {
        Assert.True(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("read", "read_file", "{}"),
            "ask"));
        Assert.False(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("write", "write_file", "{}"),
            "ask"));
        Assert.False(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("command", "run_command", "{}"),
            "ask"));
    }

    [Fact]
    public void PlanMode_AllowsPlanningButDeniesMutations()
    {
        Assert.True(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("plan", "plan_task", "{}"),
            "plan"));
        Assert.True(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("read", "grep_search", "{}"),
            "plan"));
        Assert.False(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("write", "write_file", "{}"),
            "plan"));
    }

    [Fact]
    public void FullMode_AllowsWorkspaceActions()
    {
        Assert.True(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("write", "write_file", "{}"),
            "full"));
        Assert.True(AgentLoopEngine.IsToolAllowedForMode(
            new AgentToolCall("command", "run_command", "{}"),
            "full"));
    }
}
