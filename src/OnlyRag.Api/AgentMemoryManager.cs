using System.Text;
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
        if (Steps.Count == 0) return "Nessun piano attivo.";
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
            sb.AppendLine($"{mark} {s.Description}");
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
        sb.AppendLine("=== AGENT WORKING MEMORY ===");

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

        if (CurrentPlan.Steps.Count > 0)
        {
            sb.AppendLine("Piano Attivo:");
            sb.AppendLine(CurrentPlan.ToMarkdownSummary());
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
        summarySb.AppendLine("[CONTESTO SINTETIZZATO DALL'AGENTE]");
        summarySb.AppendLine($"Sintesi automatica di {removeCount} passaggi intermedi di esecuzione:");

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

        logger?.LogInfo("AgentMemory", $"[COMPRESSIONE CONTESTO] Sintetizzati {removeCount} messaggi intermedi in 1 blocco di contesto strutturato.");
        return true;
    }

    public void PruneHistory(List<OllamaChatMessage> messages, int maxMessagesHardLimit = 30)
    {
        if (messages.Count <= 12) return;

        const int maxObsLength = 600;
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
                        string headSnippet = body.Substring(0, 250);
                        string tailSnippet = body.Substring(body.Length - 250);
                        string compressedBody = $"{headSnippet}\n... [Osservazione intermedia sintetizzata dalla memoria gerarchica ({body.Length} caratteri)] ...\n{tailSnippet}";
                        messages[i] = new OllamaChatMessage("user", header + compressedBody);
                        truncatedCount++;
                    }
                }
            }
        }

        if (truncatedCount > 0)
        {
            logger?.LogInfo("AgentMemory", $"[MEMORIA GERARCHICA] Sintetizzate {truncatedCount} osservazioni voluminose storiche.");
        }

        if (messages.Count > maxMessagesHardLimit)
        {
            CompressContext(messages, maxMessagesHardLimit - 5);
        }
    }
}
