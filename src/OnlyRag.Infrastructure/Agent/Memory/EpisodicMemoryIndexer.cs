using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Memory;

public sealed class EpisodicMemoryIndexer
{
    private readonly IAgentEpisodicMemoryService memoryService;

    public EpisodicMemoryIndexer(IAgentEpisodicMemoryService memoryService)
    {
        this.memoryService = memoryService;
    }

    public async Task IndexRunMemoryAsync(
        AgentRunSnapshot runSnapshot,
        IReadOnlyList<AgentRunTraceEvent> traceEvents,
        CancellationToken cancellationToken = default)
    {
        if (runSnapshot is null || string.IsNullOrWhiteSpace(runSnapshot.RunId) || string.IsNullOrWhiteSpace(runSnapshot.Goal))
        {
            return;
        }

        var keyFacts = traceEvents
            .Where(e => e.Success is true && !string.IsNullOrWhiteSpace(e.ToolName))
            .Select(e => $"Utensile {e.ToolName}: {e.Observation ?? e.Decision}")
            .Take(5)
            .ToList();

        string summary = !string.IsNullOrWhiteSpace(runSnapshot.FinalResponse)
            ? runSnapshot.FinalResponse
            : $"Esecuzione completata con stato {runSnapshot.Phase}. {runSnapshot.ToolCallsUsed} chiamate tool eseguite.";

        var memory = new AgentEpisodicMemory(
            runSnapshot.RunId,
            runSnapshot.Goal,
            summary,
            keyFacts,
            runSnapshot.UpdatedAtUtc);

        await memoryService.SaveMemoryAsync(memory, cancellationToken);
    }
}
