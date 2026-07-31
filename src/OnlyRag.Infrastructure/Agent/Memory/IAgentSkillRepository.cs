using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent.Memory;

public interface IAgentSkillRepository
{
    Task SaveSkillAsync(AgentSkillRecord skill, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentSkillRecord>> SearchRelevantSkillsAsync(string contextQuery, int topK = 5, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentSkillRecord>> ListSkillsAsync(CancellationToken cancellationToken = default);
}
