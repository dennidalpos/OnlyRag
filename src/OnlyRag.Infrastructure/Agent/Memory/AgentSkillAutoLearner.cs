using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Infrastructure.Agent.Memory;

public sealed class AgentSkillAutoLearner : IAgentSkillAutoLearner
{
    private readonly IAgentSkillRepository skillRepository;
    private readonly ILoggingService? logger;

    public AgentSkillAutoLearner(
        IAgentSkillRepository skillRepository,
        ILoggingService? logger = null)
    {
        this.skillRepository = skillRepository;
        this.logger = logger;
    }

    public async Task ExtractAndSaveSkillAsync(
        string goal,
        IReadOnlyList<AgentToolResult> toolResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(goal) || toolResults.Count == 0) return;

        var successfulEdits = toolResults
            .Where(r => r.Success && !string.IsNullOrWhiteSpace(r.DiffPatch))
            .ToList();

        if (successfulEdits.Count == 0) return;

        try
        {
            string category = InferCategory(goal);
            string skillId = $"auto_skill_{Math.Abs(goal.GetHashCode())}";
            string name = $"Pattern: {Truncate(goal, 60)}";
            string patternDescription = goal.Trim();

            var solutionLines = new List<string>();
            foreach (var edit in successfulEdits.Take(3))
            {
                if (!string.IsNullOrWhiteSpace(edit.DiffPatch))
                {
                    solutionLines.Add(edit.DiffPatch.Trim());
                }
            }

            string solutionTemplate = string.Join("\n---\n", solutionLines);
            if (solutionTemplate.Length > 2000)
            {
                solutionTemplate = solutionTemplate[..2000] + "\n...[truncated]";
            }

            var record = new AgentSkillRecord(
                SkillId: skillId,
                Name: name,
                Category: category,
                PatternDescription: patternDescription,
                SolutionTemplate: solutionTemplate,
                CreatedAtUtc: DateTimeOffset.UtcNow);

            await skillRepository.SaveSkillAsync(record, cancellationToken);
            logger?.LogInfo("SkillAutoLearner", $"[AUTO SKILL LEARNING] Salva skill automatica '{name}' (categoria: {category}) nel repository.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning("SkillAutoLearner", $"Impossibile estrarre ed apprendere la skill automatica: {ex.Message}");
        }
    }

    private static string InferCategory(string goal)
    {
        string lower = goal.ToLowerInvariant();
        if (lower.Contains("fix") || lower.Contains("bug") || lower.Contains("errore")) return "BugFix";
        if (lower.Contains("refactor") || lower.Contains("clean")) return "Refactoring";
        if (lower.Contains("test") || lower.Contains("assert")) return "Testing";
        if (lower.Contains("ui") || lower.Contains("component") || lower.Contains("css")) return "FrontendUI";
        return "Architecture";
    }

    private static string Truncate(string input, int maxLen)
    {
        if (input.Length <= maxLen) return input;
        return input[..maxLen] + "...";
    }
}
