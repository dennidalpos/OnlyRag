using OnlyRag.Core;
using OnlyRag.Infrastructure.Agents;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class MultiAgentOrchestratorServiceTests
{
    private readonly MultiAgentOrchestratorService _orchestrator = new();

    [Fact]
    public async Task StartOrchestrationAsync_CreatesGraphAndDecomposesTask()
    {
        var request = new MultiAgentOrchestrationRequest("Implementare sistema di fatturazione elettronica RAG");
        MultiAgentOrchestrationStatus status = await _orchestrator.StartOrchestrationAsync(request);

        Assert.NotNull(status);
        Assert.False(string.IsNullOrWhiteSpace(status.OrchestrationId));
        Assert.Equal(request.OverallGoal, status.OverallGoal);
        Assert.Equal(4, status.Subtasks.Count);
        Assert.NotEmpty(status.Messages);

        // Fetch status
        MultiAgentOrchestrationStatus? retrieved = await _orchestrator.GetStatusAsync(status.OrchestrationId);
        Assert.NotNull(retrieved);
        Assert.Equal(status.OrchestrationId, retrieved.OrchestrationId);
    }
}
