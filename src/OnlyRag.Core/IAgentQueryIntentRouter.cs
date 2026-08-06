namespace OnlyRag.Core;

public interface IAgentQueryIntentRouter
{
    Task<AgentQueryIntentResult> RouteIntentAsync(string userPrompt, string? workspaceRoot = null, CancellationToken cancellationToken = default);
}
