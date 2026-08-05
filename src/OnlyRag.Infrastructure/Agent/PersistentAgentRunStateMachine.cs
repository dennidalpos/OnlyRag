using OnlyRag.Core;
using System.Text.Json;

namespace OnlyRag.Infrastructure.Agent;

/// <summary>
/// Enforces the durable lifecycle of an agent run. The LLM may suggest actions,
/// but it cannot skip observation, verification, or recovery after an action.
/// </summary>
public sealed class PersistentAgentRunStateMachine
{
    private static readonly IReadOnlyDictionary<AgentRunPhase, AgentRunPhase[]> AllowedTransitions =
        new Dictionary<AgentRunPhase, AgentRunPhase[]>
        {
            [AgentRunPhase.Plan] = [AgentRunPhase.Act, AgentRunPhase.Finalize, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Act] = [AgentRunPhase.Observe, AgentRunPhase.Recover, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Observe] = [AgentRunPhase.Verify, AgentRunPhase.Recover, AgentRunPhase.Finalize, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Verify] = [AgentRunPhase.Plan, AgentRunPhase.Finalize, AgentRunPhase.Recover, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Recover] = [AgentRunPhase.Plan, AgentRunPhase.Act, AgentRunPhase.Finalize, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Finalize] = [AgentRunPhase.Completed, AgentRunPhase.Failed, AgentRunPhase.Cancelled],
            [AgentRunPhase.Completed] = [],
            [AgentRunPhase.Failed] = [],
            [AgentRunPhase.Cancelled] = []
        };

    public PersistentAgentRunStateMachine(AgentRunSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public AgentRunSnapshot Snapshot { get; private set; }

    public AgentRunTransition TransitionTo(AgentRunPhase nextPhase, string reason, DateTimeOffset now)
    {
        if (!AllowedTransitions.TryGetValue(Snapshot.Phase, out AgentRunPhase[]? allowed) || !allowed.Contains(nextPhase))
        {
            throw new InvalidOperationException($"Invalid agent run transition: {Snapshot.Phase} -> {nextPhase}.");
        }

        if (nextPhase is AgentRunPhase.Finalize or AgentRunPhase.Completed && !CanFinalize())
        {
            throw new InvalidOperationException("Agent run cannot finalize because required completion criteria have not passed runtime verification.");
        }

        AgentRunTransition transition = new(Snapshot.RunId, Snapshot.Phase, nextPhase, reason, now);
        Snapshot = Snapshot with { Phase = nextPhase, UpdatedAtUtc = now };
        return transition;
    }

    public void ConsumeToolCall(DateTimeOffset now)
    {
        EnsureWithinTimeBudget(now);
        if (Snapshot.ToolCallsUsed >= Snapshot.Budget.MaxToolCalls)
        {
            throw new AgentRunBudgetExceededException("tool_calls", Snapshot.Budget.MaxToolCalls);
        }

        Snapshot = Snapshot with { ToolCallsUsed = Snapshot.ToolCallsUsed + 1, UpdatedAtUtc = now };
    }

    public void ConsumeEstimatedTokens(int tokens, DateTimeOffset now)
    {
        EnsureWithinTimeBudget(now);
        int total = checked(Snapshot.EstimatedTokensUsed + Math.Max(0, tokens));
        if (total > Snapshot.Budget.MaxEstimatedTokens)
        {
            throw new AgentRunBudgetExceededException("estimated_tokens", Snapshot.Budget.MaxEstimatedTokens);
        }

        Snapshot = Snapshot with { EstimatedTokensUsed = total, UpdatedAtUtc = now };
    }

    public void ReplaceMessages(IReadOnlyList<string> messages, DateTimeOffset now)
    {
        Snapshot = Snapshot with { Messages = messages, UpdatedAtUtc = now };
    }

    public void SetOutcome(string? finalResponse, string? error, DateTimeOffset now)
    {
        Snapshot = Snapshot with { FinalResponse = finalResponse, LastError = error, UpdatedAtUtc = now };
    }

    public void RecordVerification(AgentToolCall toolCall, AgentToolResult result, DateTimeOffset now)
    {
        List<AgentCompletionVerification> verifications = Snapshot.EffectiveCompletionVerifications.ToList();
        foreach (AgentCompletionCriterion criterion in Snapshot.EffectiveCompletionCriteria)
        {
            if (!Matches(criterion, toolCall)) continue;

            verifications.RemoveAll(verification => string.Equals(verification.CriterionId, criterion.Id, StringComparison.Ordinal));
            verifications.Add(new AgentCompletionVerification(
                criterion.Id,
                result.Success ? AgentCompletionVerificationStatus.Passed : AgentCompletionVerificationStatus.Failed,
                toolCall.CallId,
                toolCall.ToolName,
                result.Success ? result.Output : result.Error ?? result.Output,
                now));
        }

        Snapshot = Snapshot with { CompletionVerifications = verifications, UpdatedAtUtc = now };
    }

    public bool CanFinalize()
    {
        return Snapshot.EffectiveCompletionCriteria
            .Where(criterion => criterion.Required)
            .All(criterion => Snapshot.EffectiveCompletionVerifications.Any(verification =>
                string.Equals(verification.CriterionId, criterion.Id, StringComparison.Ordinal)
                && verification.Status == AgentCompletionVerificationStatus.Passed));
    }

    public IReadOnlyList<AgentCompletionCriterion> GetPendingRequiredCriteria() =>
        Snapshot.EffectiveCompletionCriteria
            .Where(criterion => criterion.Required && !Snapshot.EffectiveCompletionVerifications.Any(verification =>
                string.Equals(verification.CriterionId, criterion.Id, StringComparison.Ordinal)
                && verification.Status == AgentCompletionVerificationStatus.Passed))
            .ToList();

    private static bool Matches(AgentCompletionCriterion criterion, AgentToolCall toolCall)
    {
        if (!string.Equals(criterion.ExpectedToolName, toolCall.ToolName, StringComparison.OrdinalIgnoreCase)) return false;
        if (criterion.VerificationKind != AgentCompletionVerificationKind.Command) return true;
        if (!string.Equals(toolCall.ToolName, "run_command", StringComparison.OrdinalIgnoreCase)) return false;

        string? command = GetCommand(toolCall.ArgumentsJson);
        return !string.IsNullOrWhiteSpace(command)
            && (string.IsNullOrWhiteSpace(criterion.ExpectedCommand)
                ? IsRecognizedVerificationCommand(command)
                : string.Equals(Normalize(command), Normalize(criterion.ExpectedCommand), StringComparison.Ordinal));
    }

    private static string? GetCommand(string argumentsJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(argumentsJson);
            foreach (string propertyName in new[] { "commandLine", "command", "cmd", "script" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String)
                {
                    return property.GetString();
                }
            }
        }
        catch (JsonException) { }

        return null;
    }

    private static bool IsRecognizedVerificationCommand(string command)
    {
        string normalized = Normalize(command);
        return normalized.Contains(" test", StringComparison.Ordinal)
            || normalized.Contains(" build", StringComparison.Ordinal)
            || normalized.Contains(" lint", StringComparison.Ordinal)
            || normalized.Contains(" typecheck", StringComparison.Ordinal)
            || normalized.Contains("invoke-gate", StringComparison.Ordinal);
    }

    private static string Normalize(string value) => string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public void EnsureWithinTimeBudget(DateTimeOffset now)
    {
        if (now - Snapshot.StartedAtUtc > Snapshot.Budget.EffectiveMaxDuration)
        {
            throw new AgentRunBudgetExceededException("duration", (int)Snapshot.Budget.EffectiveMaxDuration.TotalSeconds);
        }
    }

    public static int EstimateTokens(string value) => string.IsNullOrEmpty(value) ? 0 : (value.Length + 3) / 4;
}

public sealed class AgentRunBudgetExceededException(string dimension, int limit)
    : InvalidOperationException($"Agent run budget exceeded for {dimension} (limit: {limit}).")
{
}
