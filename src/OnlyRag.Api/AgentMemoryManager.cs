using System.Text;
using System.Text.Json;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;

namespace OnlyRag.Api;

public sealed class AgentPlanStep
{
    public required string Id { get; set; }
    public required string Description { get; set; }
    public string Status { get; set; } = "pending"; // pending, in_progress, completed, failed
}

public sealed class AgentPlan
{
    public List<AgentPlanStep> Steps { get; set; } = new();
    public string ActiveStepId { get; set; } = string.Empty;

    public string ToMarkdownSummary()
    {
        if (Steps.Count == 0) return "No active plan.";
        var sb = new StringBuilder();
        foreach (var s in Steps)
        {
            string mark = s.Status switch
            {
                "completed" => "[x]",
                "in_progress" => "[>]",
                "failed" => "[!]",
                _ => "[ ]"
            };
            sb.AppendLine($"{mark} Step {s.Id}: {s.Description}");
        }
        return sb.ToString().TrimEnd();
    }
}

internal sealed class AgentMemoryManager
{
    private readonly HashSet<string> modifiedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> keyFacts = new();
    private readonly List<AgentEpisodicMemory> recalledMemories = new();
    private readonly ILoggingService? logger;

    public AgentPlan CurrentPlan { get; private set; } = new();

    public AgentMemoryManager(ILoggingService? logger = null)
    {
        this.logger = logger;
    }

    public void RegisterModifiedFile(string relativePath)
    {
        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            modifiedFiles.Add(relativePath.Replace('\\', '/'));
        }
    }

    public void AddKeyFact(string fact)
    {
        if (!string.IsNullOrWhiteSpace(fact) && !keyFacts.Contains(fact))
        {
            keyFacts.Add(fact);
        }
    }

    public void AddRecalledMemories(IEnumerable<AgentEpisodicMemory> memories)
    {
        foreach (var mem in memories)
        {
            if (!recalledMemories.Any(m => m.SessionId.Equals(mem.SessionId, StringComparison.OrdinalIgnoreCase)))
            {
                recalledMemories.Add(mem);
            }
        }
    }

    public void UpdatePlan(IEnumerable<AgentPlanStep> newSteps, string? activeStepId = null)
    {
        CurrentPlan.Steps = newSteps.ToList();
        if (!string.IsNullOrWhiteSpace(activeStepId))
        {
            CurrentPlan.ActiveStepId = activeStepId;
        }
    }

    public void UpdatePlanFromToolCall(JsonElement root)
    {
        var stepsList = new List<AgentPlanStep>();
        if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == JsonValueKind.Array)
        {
            int idx = 1;
            foreach (var item in stepsProp.EnumerateArray())
            {
                string desc = item.ValueKind == JsonValueKind.Object
                    ? (item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : item.ToString())
                    : item.GetString() ?? "";

                if (!string.IsNullOrWhiteSpace(desc))
                {
                    stepsList.Add(new AgentPlanStep
                    {
                        Id = idx.ToString(),
                        Description = desc.Trim(),
                        Status = idx == 1 ? "in_progress" : "pending"
                    });
                    idx++;
                }
            }
        }

        if (stepsList.Count > 0)
        {
            CurrentPlan.Steps = stepsList;
            CurrentPlan.ActiveStepId = stepsList[0].Id;
            logger?.LogInfo("AgentMemory", $"[PLAN SCRATCHPAD UPDATED] Caricato nuovo piano con {stepsList.Count} passaggi.");
        }
    }

    public void UpdateStepStatus(string stepId, string status, string? learnings = null)
    {
        var step = CurrentPlan.Steps.FirstOrDefault(s => s.Id.Equals(stepId, StringComparison.OrdinalIgnoreCase));
        if (step != null)
        {
            step.Status = status.ToLowerInvariant();
            logger?.LogInfo("AgentMemory", $"[STEP STATUS UPDATED] Step {stepId} -> {status}");

            if (status.Equals("completed", StringComparison.OrdinalIgnoreCase))
            {
                var nextStep = CurrentPlan.Steps.FirstOrDefault(s => s.Status == "pending");
                if (nextStep != null)
                {
                    nextStep.Status = "in_progress";
                    CurrentPlan.ActiveStepId = nextStep.Id;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(learnings))
        {
            AddKeyFact($"[Learning Step {stepId}] {learnings}");
        }
    }

    public IReadOnlyCollection<string> GetModifiedFiles() => modifiedFiles;
    public IReadOnlyCollection<string> GetKeyFacts() => keyFacts;

    private readonly HashSet<string> exploredPaths = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterExploredPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            exploredPaths.Add(path.Replace('\\', '/'));
        }
    }

    public string BuildContextSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== AGENT WORKING MEMORY & LIVE PLAN SCRATCHPAD ===");

        if (CurrentPlan.Steps.Count > 0)
        {
            sb.AppendLine("LIVE PLAN SCRATCHPAD:");
            sb.AppendLine(CurrentPlan.ToMarkdownSummary());
            sb.AppendLine();
        }

        if (recalledMemories.Count > 0)
        {
            sb.AppendLine($"Memorie Episodiche Richiamate da Sessioni Precedenti ({recalledMemories.Count}):");
            foreach (var mem in recalledMemories)
            {
                sb.AppendLine($"  * [Goal: {mem.Goal}] Summary: {mem.Summary}");
            }
        }

        if (exploredPaths.Count > 0)
        {
            sb.AppendLine($"Percorsi e File Già Ispezionati/Esplorati ({exploredPaths.Count}):");
            foreach (var p in exploredPaths.Take(25))
            {
                sb.AppendLine($"  - {p}");
            }
        }

        if (modifiedFiles.Count > 0)
        {
            sb.AppendLine($"File Modificati nel Workspace ({modifiedFiles.Count}):");
            foreach (var f in modifiedFiles)
            {
                sb.AppendLine($"  - {f}");
            }
        }

        if (keyFacts.Count > 0)
        {
            sb.AppendLine("Fatti e Risultati Chiave:");
            foreach (var fact in keyFacts)
            {
                sb.AppendLine($"  * {fact}");
            }
        }

        return sb.ToString().TrimEnd();
    }

    public bool CompressContext(List<OllamaChatMessage> messages, int maxMessagesThreshold = 20)
    {
        if (messages.Count <= maxMessagesThreshold) return false;

        int preserveRecentCount = 6;
        int startIndex = 2; // Keep system prompt (0) and initial user goal (1)
        int removeCount = messages.Count - startIndex - preserveRecentCount;

        if (removeCount <= 0) return false;

        var historyToCompress = messages.GetRange(startIndex, removeCount);
        var summarySb = new StringBuilder();
        summarySb.AppendLine("[AGENT SYNTHESIZED CONTEXT]");
        summarySb.AppendLine($"Automatic summary of {removeCount} intermediate execution steps:");

        foreach (var msg in historyToCompress)
        {
            if (msg.Role == "assistant")
            {
                string snippet = msg.Content.Length > 150 ? $"{msg.Content[..150]}..." : msg.Content;
                summarySb.AppendLine($"- LLM: {snippet.Replace('\n', ' ')}");
            }
            else if (msg.Role == "user" && msg.Content.StartsWith("[TOOL RESULT", StringComparison.OrdinalIgnoreCase))
            {
                int endHeader = msg.Content.IndexOf(']');
                string header = endHeader > 0 ? msg.Content.Substring(0, endHeader + 1) : "[TOOL RESULT]";
                summarySb.AppendLine($"  * {header}");
            }
        }

        summarySb.AppendLine();
        summarySb.AppendLine(BuildContextSummary());

        messages.RemoveRange(startIndex, removeCount);
        messages.Insert(startIndex, new OllamaChatMessage("system", summarySb.ToString().TrimEnd()));

        logger?.LogInfo("AgentMemory", $"[CONTEXT COMPRESSION] Synthesized {removeCount} intermediate messages into 1 structured context block.");
        return true;
    }

    public void PruneHistory(List<OllamaChatMessage> messages, int maxMessagesHardLimit = 30)
    {
        if (messages.Count <= 12) return;

        const int maxObsLength = 800;
        int preserveRecentIdx = Math.Max(2, messages.Count - 6);
        int truncatedCount = 0;

        for (int i = 2; i < preserveRecentIdx; i++)
        {
            var msg = messages[i];
            if (msg.Role == "user" && msg.Content.StartsWith("[TOOL RESULT", StringComparison.OrdinalIgnoreCase) && msg.Content.Length > maxObsLength)
            {
                string originalText = msg.Content;
                int outputIdx = originalText.IndexOf("Output:\n", StringComparison.OrdinalIgnoreCase);
                if (outputIdx != -1)
                {
                    string header = originalText.Substring(0, outputIdx + 8);
                    string body = originalText.Substring(outputIdx + 8);

                    if (body.Length > maxObsLength)
                    {
                        string headSnippet = body.Substring(0, 300);
                        string tailSnippet = body.Substring(body.Length - 250);

                        // Preserve lines containing [STDERR], Error, Exception or fail if present in the body
                        string errorLines = string.Empty;
                        if (body.Contains("[STDERR]", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            body.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                        {
                            var errMatches = body.Split('\n')
                                .Where(l => l.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                                           l.Contains("at ", StringComparison.OrdinalIgnoreCase) ||
                                           l.Contains("[STDERR]", StringComparison.OrdinalIgnoreCase) ||
                                           l.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                                           l.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
                                .Take(10);
                            errorLines = "\n[PRESERVED ERROR EXTRACT]:\n" + string.Join("\n", errMatches);
                        }

                        string compressedBody = $"{headSnippet}\n... [Intermediate observation synthesized by hierarchical memory ({body.Length} chars)] ...{errorLines}\n{tailSnippet}";
                        messages[i] = new OllamaChatMessage("user", header + compressedBody);
                        truncatedCount++;
                    }
                }
            }
        }

        if (truncatedCount > 0)
        {
            logger?.LogInfo("AgentMemory", $"[HIERARCHICAL MEMORY] Synthesized {truncatedCount} large historical observations, preserving error traces.");
        }

        if (messages.Count > maxMessagesHardLimit)
        {
            CompressContext(messages, maxMessagesHardLimit - 5);
        }
    }
}
