using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Memory;

public interface IAgentEpisodicMemoryService
{
    Task SaveMemoryAsync(
        AgentEpisodicMemory memory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentEpisodicMemory>> SearchRelevantMemoriesAsync(
        string currentGoal,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
