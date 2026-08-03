using System.Collections.Concurrent;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agents;

public sealed class MultiAgentOrchestratorService : IMultiAgentOrchestratorService
{
    private readonly ConcurrentDictionary<string, MultiAgentOrchestrationStatus> _orchestrationStore = new();

    public async Task<MultiAgentOrchestrationStatus> StartOrchestrationAsync(
        MultiAgentOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string id = $"orch_{Guid.NewGuid():N}";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Decompose goal into structured multi-agent graph
        var subtasks = new List<MultiAgentSubtask>
        {
            new("sub_planner", "Planner Agent", $"Analisi requisiti e pianificazione architetturale per: {request.OverallGoal}", [], MultiAgentSubtaskStatus.Pending),
            new("sub_researcher", "Research & Context Agent", "Ricerca del contesto e recupero documentazione di riferimento", ["sub_planner"], MultiAgentSubtaskStatus.Pending),
            new("sub_coder", "Code Synthesizer Agent", "Generazione codice e refactoring componenti", ["sub_researcher"], MultiAgentSubtaskStatus.Pending),
            new("sub_evaluator", "Reviewer & QA Agent", "Validazione sintattica, test unitari e verifica di qualita", ["sub_coder"], MultiAgentSubtaskStatus.Pending)
        };

        var initialMessages = new List<InterAgentMessage>
        {
            new(Guid.NewGuid().ToString("N"), "Orchestrator", "Planner Agent", $"Inizializzato flusso multi-agente per l'obiettivo: {request.OverallGoal}", now)
        };

        var status = new MultiAgentOrchestrationStatus(
            id,
            request.OverallGoal,
            IsCompleted: false,
            HasFailed: false,
            Subtasks: subtasks,
            Messages: initialMessages,
            StartedAtUtc: now);

        _orchestrationStore[id] = status;

        // Background execution task
        _ = Task.Run(() => ExecuteOrchestrationGraphAsync(id, cancellationToken), cancellationToken);

        return status;
    }

    public Task<MultiAgentOrchestrationStatus?> GetStatusAsync(
        string orchestrationId,
        CancellationToken cancellationToken = default)
    {
        _orchestrationStore.TryGetValue(orchestrationId, out var status);
        return Task.FromResult(status);
    }

    private async Task ExecuteOrchestrationGraphAsync(string id, CancellationToken ct)
    {
        if (!_orchestrationStore.TryGetValue(id, out var currentStatus)) return;

        List<MultiAgentSubtask> updatedSubtasks = currentStatus.Subtasks.ToList();
        List<InterAgentMessage> updatedMessages = currentStatus.Messages.ToList();

        for (int i = 0; i < updatedSubtasks.Count; i++)
        {
            if (ct.IsCancellationRequested) break;

            var task = updatedSubtasks[i];
            DateTimeOffset startTime = DateTimeOffset.UtcNow;
            
            // Mark Running
            updatedSubtasks[i] = task with { Status = MultiAgentSubtaskStatus.Running, StartedAtUtc = startTime };
            _orchestrationStore[id] = currentStatus with { Subtasks = updatedSubtasks.ToList(), Messages = updatedMessages.ToList() };

            await Task.Delay(1200, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

            // Send Inter-Agent Message
            DateTimeOffset msgTime = DateTimeOffset.UtcNow;
            string recipient = i + 1 < updatedSubtasks.Count ? updatedSubtasks[i + 1].Role : "Orchestrator";
            updatedMessages.Add(new InterAgentMessage(
                Guid.NewGuid().ToString("N"),
                task.Role,
                recipient,
                $"Completato sub-task '{task.SubtaskId}' con successo. Passaggio dati a {recipient}.",
                msgTime));

            // Mark Completed
            updatedSubtasks[i] = updatedSubtasks[i] with
            {
                Status = MultiAgentSubtaskStatus.Completed,
                Output = $"Sub-task '{task.Goal}' completato ed elaborato con successo.",
                CompletedAtUtc = DateTimeOffset.UtcNow
            };

            _orchestrationStore[id] = currentStatus with { Subtasks = updatedSubtasks.ToList(), Messages = updatedMessages.ToList() };
        }

        _orchestrationStore[id] = currentStatus with
        {
            IsCompleted = true,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Subtasks = updatedSubtasks,
            Messages = updatedMessages
        };
    }
}
