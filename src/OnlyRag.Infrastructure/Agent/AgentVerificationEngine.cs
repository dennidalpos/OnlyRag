using System.Text.Json;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent;

public sealed class AgentVerificationEngine : IAgentVerificationEngine
{
    public Task<AgentVerificationEvidence> VerifyStepAsync(
        AgentTaskStep step,
        AgentToolCall toolCall,
        AgentToolResult toolResult,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(toolCall);
        ArgumentNullException.ThrowIfNull(toolResult);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string verificationId = $"verif_{Guid.NewGuid():N}";
        string criterionId = step.StepId;
        string toolName = toolCall.ToolName;
        string? command = GetCommandFromArgs(toolCall.ArgumentsJson);

        bool passed = toolResult.Success;
        string details = toolResult.Success ? toolResult.Output : (toolResult.Error ?? toolResult.Output);

        // Verification checks based on tool type
        if (string.Equals(toolName, "run_command", StringComparison.OrdinalIgnoreCase))
        {
            if (!toolResult.Success || details.Contains("FAILED", StringComparison.OrdinalIgnoreCase) || details.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase))
            {
                passed = false;
            }
        }
        else if (string.Equals(toolName, "replace_file_content", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(toolName, "write_to_file", StringComparison.OrdinalIgnoreCase))
        {
            passed = toolResult.Success && !string.IsNullOrWhiteSpace(toolResult.Output);
        }

        var evidence = new AgentVerificationEvidence(
            verificationId,
            criterionId,
            toolName,
            toolCall.CallId,
            toolName,
            command,
            passed,
            details,
            now);

        return Task.FromResult(evidence);
    }

    public Task<bool> VerifyPlanCompletionAsync(
        AgentTypedPlan plan,
        IReadOnlyList<AgentVerificationEvidence> evidences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidences);

        if (!plan.IsBinding) return Task.FromResult(true);

        foreach (AgentCompletionCriterion criterion in plan.MandatoryVerifications.Where(c => c.Required))
        {
            bool hasPassedEvidence = evidences.Any(ev =>
                ev.Passed &&
                (string.Equals(ev.ToolName, criterion.ExpectedToolName, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrWhiteSpace(criterion.ExpectedCommand) && ev.Command != null && ev.Command.Contains(criterion.ExpectedCommand, StringComparison.OrdinalIgnoreCase))));

            if (!hasPassedEvidence)
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    private static string? GetCommandFromArgs(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            foreach (string prop in new[] { "commandLine", "command", "cmd", "script" })
            {
                if (doc.RootElement.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.String)
                {
                    return elem.GetString();
                }
            }
        }
        catch (JsonException) { }

        return null;
    }
}
