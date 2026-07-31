using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Memory;

public interface IAgentSkillAutoLearner
{
    Task ExtractAndSaveSkillAsync(
        string goal,
        IReadOnlyList<AgentToolResult> toolResults,
        CancellationToken cancellationToken = default);
}
