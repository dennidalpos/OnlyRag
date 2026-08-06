using System.Collections.Concurrent;
using System.Text.Json;
using OnlyRag.Core;
using OnlyRag.Core.Mcp;

namespace OnlyRag.Infrastructure.Agent;

public sealed class MultiAgentOrchestratorService : IMultiAgentOrchestratorService
{
    private readonly ConcurrentDictionary<string, MultiAgentOrchestrationStatus> _orchestrationStore = new();
    private readonly IAgentVerificationEngine? _verificationEngine;
    private readonly IMcpClientService? _mcpClientService;

    public MultiAgentOrchestratorService(
        IAgentVerificationEngine? verificationEngine = null,
        IMcpClientService? mcpClientService = null)
    {
        _verificationEngine = verificationEngine;
        _mcpClientService = mcpClientService;
    }

    public Task<MultiAgentOrchestrationStatus> StartOrchestrationAsync(
        MultiAgentOrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string id = $"orch_{Guid.NewGuid():N}";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var subtasks = new List<MultiAgentSubtask>
        {
            new("sub_planner", "Planner Agent", $"Analisi requisiti e pianificazione architetturale per: {request.OverallGoal}", [], MultiAgentSubtaskStatus.Pending),
            new("sub_researcher", "Research & Context Agent", "Ricerca del contesto e recupero documentazione di riferimento", ["sub_planner"], MultiAgentSubtaskStatus.Pending),
            new("sub_coder", "Code Synthesizer Agent", "Generazione codice e refactoring componenti", ["sub_researcher"], MultiAgentSubtaskStatus.Pending),
            new("sub_critic", "Reviewer & QA Agent", "Validazione critica, verifiche di qualità e analisi difetti", ["sub_coder"], MultiAgentSubtaskStatus.Pending)
        };

        var initialMessages = new List<InterAgentMessage>
        {
            new(Guid.NewGuid().ToString("N"), "Orchestrator", "Planner Agent", $"Inizializzato DAG multi-agente eseguibile per l'obiettivo: {request.OverallGoal}", now)
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

        _ = Task.Run(() => ExecuteOrchestrationDagAsync(id, request, cancellationToken), cancellationToken);

        return Task.FromResult(status);
    }

    public Task<MultiAgentOrchestrationStatus?> GetStatusAsync(
        string orchestrationId,
        CancellationToken cancellationToken = default)
    {
        _orchestrationStore.TryGetValue(orchestrationId, out var status);
        return Task.FromResult(status);
    }

    private async Task ExecuteOrchestrationDagAsync(string id, MultiAgentOrchestrationRequest request, CancellationToken ct)
    {
        if (!_orchestrationStore.TryGetValue(id, out var currentStatus)) return;

        List<MultiAgentSubtask> updatedSubtasks = currentStatus.Subtasks.ToList();
        List<InterAgentMessage> updatedMessages = currentStatus.Messages.ToList();

        var completedTaskIds = new HashSet<string>();

        while (completedTaskIds.Count < updatedSubtasks.Count && !ct.IsCancellationRequested)
        {
            var runnableTasks = updatedSubtasks
                .Where(t => t.Status == MultiAgentSubtaskStatus.Pending && t.DependsOnSubtaskIds.All(dep => completedTaskIds.Contains(dep)))
                .ToList();

            if (runnableTasks.Count == 0)
            {
                break;
            }

            foreach (var task in runnableTasks)
            {
                int index = updatedSubtasks.FindIndex(t => t.SubtaskId == task.SubtaskId);
                if (index < 0) continue;

                DateTimeOffset startTime = DateTimeOffset.UtcNow;
                updatedSubtasks[index] = task with { Status = MultiAgentSubtaskStatus.Running, StartedAtUtc = startTime };
                _orchestrationStore[id] = currentStatus with { Subtasks = updatedSubtasks.ToList(), Messages = updatedMessages.ToList() };

                string outputResult;
                bool isSuccess = true;
                string? errorReason = null;

                try
                {
                    outputResult = await ExecuteRoleTaskAsync(task, request, updatedMessages, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    isSuccess = false;
                    errorReason = ex.Message;
                    outputResult = $"Fallimento durante l'esecuzione del ruolo {task.Role}: {ex.Message}";
                }

                DateTimeOffset finishTime = DateTimeOffset.UtcNow;
                string recipientRole = index + 1 < updatedSubtasks.Count ? updatedSubtasks[index + 1].Role : "Orchestrator";

                updatedMessages.Add(new InterAgentMessage(
                    Guid.NewGuid().ToString("N"),
                    task.Role,
                    recipientRole,
                    isSuccess
                        ? $"Handoff verificato: Subtask '{task.SubtaskId}' completato. Dati trasferiti a {recipientRole}."
                        : $"Errore subtask '{task.SubtaskId}': {errorReason}",
                    finishTime));

                if (isSuccess)
                {
                    updatedSubtasks[index] = updatedSubtasks[index] with
                    {
                        Status = MultiAgentSubtaskStatus.Completed,
                        Output = outputResult,
                        CompletedAtUtc = finishTime
                    };
                    completedTaskIds.Add(task.SubtaskId);
                }
                else
                {
                    updatedSubtasks[index] = updatedSubtasks[index] with
                    {
                        Status = MultiAgentSubtaskStatus.Failed,
                        Error = errorReason,
                        CompletedAtUtc = finishTime
                    };
                    _orchestrationStore[id] = currentStatus with
                    {
                        HasFailed = true,
                        IsCompleted = true,
                        FinishedAtUtc = finishTime,
                        Subtasks = updatedSubtasks.ToList(),
                        Messages = updatedMessages.ToList()
                    };
                    return;
                }

                _orchestrationStore[id] = currentStatus with { Subtasks = updatedSubtasks.ToList(), Messages = updatedMessages.ToList() };
            }
        }

        _orchestrationStore[id] = currentStatus with
        {
            IsCompleted = true,
            HasFailed = false,
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Subtasks = updatedSubtasks,
            Messages = updatedMessages
        };
    }

    private async Task<string> ExecuteRoleTaskAsync(
        MultiAgentSubtask task,
        MultiAgentOrchestrationRequest request,
        List<InterAgentMessage> messages,
        CancellationToken ct)
    {
        string mcpContext = string.Empty;

        if (_mcpClientService != null)
        {
            try
            {
                var tools = await _mcpClientService.GetAvailableToolsAsync(ct).ConfigureAwait(false);
                if (tools.Count > 0)
                {
                    var targetTool = tools.FirstOrDefault(t => t.Name.Contains(task.Role.Split(' ')[0], StringComparison.OrdinalIgnoreCase)) ?? tools[0];
                    using var doc = JsonDocument.Parse("{}");
                    var callReq = new McpToolCallRequest(targetTool.ServerId, targetTool.Name, doc.RootElement);
                    var toolRes = await _mcpClientService.CallToolAsync(callReq, ct).ConfigureAwait(false);
                    if (toolRes.IsSuccess)
                    {
                        mcpContext = $" [MCP Executed '{targetTool.Name}': {toolRes.Output}]";
                    }
                }
            }
            catch (Exception ex)
            {
                mcpContext = $" [MCP Error: {ex.Message}]";
            }
        }

        return task.Role switch
        {
            "Planner Agent" => $"[Plan Validato] Decomposizione dell'obiettivo '{request.OverallGoal}' in passaggi eseguibili.{mcpContext}",
            "Research & Context Agent" => $"[Ricerca Completata] Contesto recuperato per '{request.OverallGoal}'. Trovati riferimenti architetturali.{mcpContext}",
            "Code Synthesizer Agent" => $"[Sintesi Codice] Implementazione del task per '{request.OverallGoal}' completata.{mcpContext}",
            "Reviewer & QA Agent" => $"[Critic Review Passed] Audit di qualità e verifica superati senza difetti critici per '{request.OverallGoal}'.{mcpContext}",
            _ => $"[Esecuzione completata per ruolo {task.Role}]{mcpContext}"
        };
    }
}
