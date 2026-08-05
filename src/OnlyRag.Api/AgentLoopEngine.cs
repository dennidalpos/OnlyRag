using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Api;

internal sealed class AgentLoopEngine
{
    private const int MaxJsonRetries = 2;
    public const int DefaultMaxIterations = 500;
    private const int DefaultNumCtx = 16384;

    private readonly IOllamaClient ollamaClient;
    private readonly IOllamaSettingsService settingsService;
    private readonly WorkspaceToolExecutor toolExecutor;
    private readonly BackgroundTaskManager taskManager;
    private readonly OnlyRag.Infrastructure.Agent.Memory.IAgentEpisodicMemoryService? episodicMemoryService;
    private readonly OnlyRag.Infrastructure.Agent.Memory.IAgentSkillRepository? skillRepository;
    private readonly OnlyRag.Infrastructure.Agent.Memory.IAgentSkillAutoLearner? skillAutoLearner;
    private readonly WorkspaceSnapshotCheckpointManager? checkpointManager;
    private readonly IAstDependencyGraphService? astGraphService;
    private readonly ILoggingService? logger;
    private readonly IAgentRunStateRepository? runStateRepository;

    private readonly ConcurrentDictionary<string, (AgentToolCall Call, TaskCompletionSource<bool> Tcs)> pendingApprovals = new();

    public AgentLoopEngine(
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService,
        WorkspaceToolExecutor toolExecutor,
        BackgroundTaskManager taskManager,
        OnlyRag.Infrastructure.Agent.Memory.IAgentEpisodicMemoryService? episodicMemoryService = null,
        OnlyRag.Infrastructure.Agent.Memory.IAgentSkillRepository? skillRepository = null,
        OnlyRag.Infrastructure.Agent.Memory.IAgentSkillAutoLearner? skillAutoLearner = null,
        WorkspaceSnapshotCheckpointManager? checkpointManager = null,
        IAstDependencyGraphService? astGraphService = null,
        ILoggingService? logger = null,
        IAgentRunStateRepository? runStateRepository = null)
    {
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
        this.toolExecutor = toolExecutor;
        this.taskManager = taskManager;
        this.episodicMemoryService = episodicMemoryService;
        this.skillRepository = skillRepository;
        this.skillAutoLearner = skillAutoLearner;
        this.checkpointManager = checkpointManager;
        this.astGraphService = astGraphService;
        this.logger = logger;
        this.runStateRepository = runStateRepository;
    }


    public bool ApproveToolCall(string callId, bool approved)
    {
        if (pendingApprovals.TryRemove(callId, out var pending))
        {
            pending.Tcs.TrySetResult(approved);
            logger?.LogInfo("AgentEngine", $"Tool call {callId} approval: {approved}");
            return true;
        }

        logger?.LogWarning("AgentEngine", $"Approval attempt for unknown callId: {callId}");
        return false;
    }

    public async IAsyncEnumerable<AgentStepEvent> RunAgentLoopAsync(
        AgentRunRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string model = string.Empty;
        string workspaceRoot = request.WorkspaceRoot ?? "";
        logger?.LogInfo("AgentEngine", $"[AGENT LOOP START] Goal: '{request.Goal}', Mode: '{request.Mode}', AutoApprove: {request.AutoApproveCommands}, Workspace: '{workspaceRoot}'");

        string? resolveError = null;
        try
        {
            model = await ResolveModelAsync(request.Model, cancellationToken);
        }
        catch (Exception ex)
        {
            resolveError = $"Error resolving LLM model for the agent: {ex.Message}";
            logger?.LogError("AgentEngine", resolveError, ex);
        }

        if (resolveError != null)
        {
            yield return new AgentStepEvent("error", resolveError);
            yield break;
        }

        var memoryManager = new AgentMemoryManager(logger);
        var mctsMachine = checkpointManager != null ? new AgentMctsStateMachine(checkpointManager, request.Goal) : null;
        if (mctsMachine != null)
        {
            logger?.LogInfo("AgentEngine", "[MCTS TREE-OF-THOUGHT ENGINE INITIALIZED] Active state tracking & candidate branch selection enabled.");
        }


        string recalledMemorySection = string.Empty;
        if (episodicMemoryService != null && !string.IsNullOrWhiteSpace(request.Goal))
        {
            try
            {
                var recalledMemories = await episodicMemoryService.SearchRelevantMemoriesAsync(request.Goal, topK: 3, cancellationToken);
                if (recalledMemories.Count > 0)
                {
                    memoryManager.AddRecalledMemories(recalledMemories);
                    var memSb = new StringBuilder("\n\n### [EPISODIC MEMORY RECALL - PAST SESSION EXPERIENCE]\n");
                    foreach (var mem in recalledMemories)
                    {
                        memSb.AppendLine($"- **Goal**: {mem.Goal}");
                        memSb.AppendLine($"  **Summary**: {mem.Summary}");
                        if (mem.KeyFacts != null && mem.KeyFacts.Count > 0)
                        {
                            memSb.AppendLine($"  **Key Facts**: {string.Join("; ", mem.KeyFacts)}");
                        }
                    }
                    recalledMemorySection = memSb.ToString();
                    logger?.LogInfo("AgentEngine", $"[EPISODIC MEMORY RECALL] Recalled {recalledMemories.Count} relevant memories from previous sessions.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("AgentEngine", $"Could not recall episodic memories: {ex.Message}");
            }
        }

        string enrichedGoal = AgentWorkspaceContextEnricher.EnrichGoalWithWorkspaceContext(request.Goal, workspaceRoot) + recalledMemorySection;

        var messages = new List<OllamaChatMessage>
        {
            new("system", GetSystemPrompt(request.Mode, request.AutoApproveCommands)),
            new("user", enrichedGoal)
        };

        PersistentAgentRunStateMachine? durableStateMachine = null;
        string runId = request.ResumeRunId?.Trim() ?? Guid.NewGuid().ToString("N");
        if (runStateRepository is not null)
        {
            if (!string.IsNullOrWhiteSpace(request.ResumeRunId))
            {
                AgentRunSnapshot? resumed = await runStateRepository.GetAsync(runId, cancellationToken);
                if (resumed is null || resumed.Phase is AgentRunPhase.Completed or AgentRunPhase.Failed or AgentRunPhase.Cancelled)
                {
                    yield return new AgentStepEvent("error", "The requested agent run cannot be resumed.", RunId: runId);
                    yield break;
                }

                if (resumed.Messages.Count == 1)
                {
                    List<OllamaChatMessage>? restoredMessages = JsonSerializer.Deserialize<List<OllamaChatMessage>>(resumed.Messages[0]);
                    if (restoredMessages is { Count: > 0 }) messages = restoredMessages;
                }

                model = resumed.Model ?? model;
                workspaceRoot = resumed.WorkspaceRoot;
                durableStateMachine = new PersistentAgentRunStateMachine(resumed);
            }
            else
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                AgentRunBudget budget = new(
                    MaxToolCalls: request.MaxToolCalls is > 0 ? request.MaxToolCalls.Value : DefaultMaxIterations,
                    MaxEstimatedTokens: request.MaxEstimatedTokens is > 0 ? request.MaxEstimatedTokens.Value : 60_000,
                    MaxDuration: request.MaxDurationSeconds is > 0 ? TimeSpan.FromSeconds(request.MaxDurationSeconds.Value) : null);
                AgentRunSnapshot created = new(
                    runId, request.Goal, request.Mode ?? "write", model, workspaceRoot, AgentRunPhase.Plan, budget,
                    ToolCallsUsed: 0, EstimatedTokensUsed: 0, now, now, LastError: null, FinalResponse: null,
                    Messages: [JsonSerializer.Serialize(messages)],
                    CompletionCriteria: NormalizeCompletionCriteria(request.CompletionCriteria));
                await runStateRepository.CreateAsync(created, cancellationToken);
                durableStateMachine = new PersistentAgentRunStateMachine(created);
                await AppendTraceAsync(runId, 0, "run_started", created.Phase, decision: request.Goal, cancellationToken: cancellationToken);
            }
        }

        if (skillRepository != null && !string.IsNullOrWhiteSpace(request.Goal))
        {
            try
            {
                var recalledSkills = await skillRepository.SearchRelevantSkillsAsync(request.Goal, topK: 3, cancellationToken);
                if (recalledSkills.Count > 0)
                {
                    var sb = new StringBuilder("### [SKILL REPOSITORY RECALL]\nSystem Skill Recipes:\n");
                    foreach (var s in recalledSkills)
                    {
                        sb.AppendLine($"- **{s.Name}** [{s.Category}]: {s.PatternDescription} -> Solution: {s.SolutionTemplate}");
                    }
                    messages.Add(new("system", sb.ToString()));
                    logger?.LogInfo("AgentEngine", $"[SKILL REPOSITORY RECALL] Recalled {recalledSkills.Count} relevant skills.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("AgentEngine", $"Could not recall skills from repository: {ex.Message}");
            }
        }

        yield return new AgentStepEvent("state_changed", "Agent run is planning the next verified action.", RunId: runId, Phase: durableStateMachine?.Snapshot.Phase);
        yield return new AgentStepEvent("thought", $"[Agent Engine] Starting goal processing in '{request.Mode}' mode with model '{model}'.", RunId: runId, Phase: durableStateMachine?.Snapshot.Phase);

        int maxIterations = (request.MaxIterations.HasValue && request.MaxIterations.Value > 0) ? request.MaxIterations.Value : DefaultMaxIterations;
        var failedToolSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executedReadOnlyCallSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentToolSignatures = new List<string>();
        var accumulatedToolResults = new List<AgentToolResult>();
        int iteration = 0;
        int jsonRetryCount = 0;
        int cycleGuardTriggerCount = 0;

        // Cache settings once before the hot loop — avoid I/O on every iteration
        OllamaSettings? cachedSettings = null;
        try
        {
            cachedSettings = await settingsService.GetAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger?.LogWarning("AgentEngine", $"Could not load Ollama settings, using defaults: {ex.Message}");
        }

        int numCtx = cachedSettings?.CodingNumCtx ?? cachedSettings?.ChatNumCtx ?? DefaultNumCtx;

        while (iteration < maxIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();
            durableStateMachine?.EnsureWithinTimeBudget(DateTimeOffset.UtcNow);

            if (durableStateMachine?.Snapshot.Phase == AgentRunPhase.Plan)
            {
                await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Act, "Selected the next action.", messages, cancellationToken);
                yield return new AgentStepEvent("state_changed", "Agent run is selecting an action.", RunId: runId, Phase: AgentRunPhase.Act);
            }

            memoryManager.PruneHistory(messages);

            string iterLabel = $"{iteration}/{maxIterations}";
            logger?.LogTrace("AgentEngine", $"[AGENT ITERATION {iterLabel}] Sending request to Ollama (Model: {model}, NumCtx: {numCtx}, Messages: {messages.Count})");

            yield return new AgentStepEvent("thought", $"[Agent Step {iterLabel}] Generating LLM response and analyzing reasoning...");

            var responseSb = new StringBuilder();
            string? streamError = null;
            Stopwatch modelResponseStopwatch = Stopwatch.StartNew();

            int maxStreamRetries = 2;
            for (int streamAttempt = 1; streamAttempt <= maxStreamRetries; streamAttempt++)
            {
                responseSb.Clear();
                streamError = null;
                IAsyncEnumerator<string>? enumerator = null;

                try
                {
                    enumerator = ollamaClient.GenerateChatStreamAsync(
                        model,
                        messages,
                        numCtx: numCtx,
                        format: AgentToolJsonSchemaBuilder.BuildToolCallJsonSchema(),
                        cancellationToken: cancellationToken)
                        .GetAsyncEnumerator(cancellationToken);
                }
                catch (Exception ex)
                {
                    streamError = $"Error querying Ollama with model '{model}': {ex.Message}";
                    logger?.LogError("AgentEngine", streamError, ex);
                }

                if (streamError != null)
                {
                    break;
                }

                if (enumerator != null)
                {
                    var chunkBatch = new StringBuilder();
                    await using (enumerator)
                    {
                        while (true)
                        {
                            string? chunk = null;
                            bool hasMore = false;
                            try
                            {
                                hasMore = await enumerator.MoveNextAsync();
                                if (hasMore) chunk = enumerator.Current;
                            }
                            catch (Exception ex)
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    streamError = $"Ollama streaming cancelled by caller.";
                                    break;
                                }
                                else if (ex is OperationCanceledException or System.IO.IOException)
                                {
                                    streamError = $"Ollama transport reset (attempt {streamAttempt}/{maxStreamRetries}): {ex.Message}";
                                    logger?.LogWarning("AgentEngine", streamError);
                                }
                                else
                                {
                                    streamError = $"Error during Ollama streaming with model '{model}': {ex.Message}";
                                    logger?.LogError("AgentEngine", streamError, ex);
                                    break;
                                }
                            }

                            if (streamError != null) break;

                            if (!hasMore)
                            {
                                if (chunkBatch.Length > 0)
                                {
                                    yield return new AgentStepEvent("thought_chunk", Content: chunkBatch.ToString());
                                    chunkBatch.Clear();
                                }
                                break;
                            }

                            if (!string.IsNullOrEmpty(chunk))
                            {
                                responseSb.Append(chunk);
                                chunkBatch.Append(chunk);

                                if (chunkBatch.Length >= 40 || chunk.Contains('\n'))
                                {
                                    yield return new AgentStepEvent("thought_chunk", Content: chunkBatch.ToString());
                                    chunkBatch.Clear();
                                }
                            }
                        }
                    }
                }

                if (streamError == null || cancellationToken.IsCancellationRequested || streamAttempt == maxStreamRetries)
                {
                    break;
                }

                logger?.LogWarning("AgentEngine", $"Retrying Ollama chat stream (attempt {streamAttempt + 1}/{maxStreamRetries}) after transient transport error...");
                await Task.Delay(500 * streamAttempt, cancellationToken);
            }

            if (streamError != null)
            {
                yield return new AgentStepEvent("error", streamError);
                yield break;
            }

            string responseText = responseSb.ToString();
            modelResponseStopwatch.Stop();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                string errEmpty = "The LLM model returned no content in the response.";
                logger?.LogWarning("AgentEngine", errEmpty);
                yield return new AgentStepEvent("error", errEmpty);
                yield break;
            }

            if (durableStateMachine is not null)
            {
                durableStateMachine.ConsumeEstimatedTokens(PersistentAgentRunStateMachine.EstimateTokens(responseText), DateTimeOffset.UtcNow);
                await SaveProgressAsync(durableStateMachine, messages, cancellationToken);
                await AppendTraceAsync(runId, iteration, "model_response", durableStateMachine.Snapshot.Phase,
                    observation: responseText, estimatedTokens: durableStateMachine.Snapshot.EstimatedTokensUsed,
                    latencyMs: modelResponseStopwatch.Elapsed.TotalMilliseconds, cancellationToken: cancellationToken);
            }

            messages.Add(new("assistant", responseText));

            var toolCalls = AgentToolCallParser.TryExtractToolCalls(responseText, logger);
            if (toolCalls.Count == 0)
            {
                if (AgentToolCallParser.LooksLikeFailedToolCall(responseText) && jsonRetryCount < MaxJsonRetries)
                {
                    jsonRetryCount++;
                    logger?.LogWarning("AgentEngine", $"[JSON PARSE RETRY {jsonRetryCount}/{MaxJsonRetries}] Detected malformed JSON in tool call, sending corrective prompt.");
                    yield return new AgentStepEvent("json_parse_warning",
                        $"⚠️ Malformed JSON in LLM response (correction attempt {jsonRetryCount}/{MaxJsonRetries}). Requesting model to retry...");
                    messages.Add(new("user",
                        "[FORMAT ERROR] Your previous response contained a tool call attempt, " +
                        "but the JSON was invalid and could not be parsed. " +
                        "Respond STRICTLY with a single ```json { \"tool\": \"tool_name\", \"arguments\": {...} } ``` block " +
                        "or an array ```json [ {\"tool\": \"...\", \"arguments\": {...}} ] ``` for parallel calls."));
                    continue;
                }

                logger?.LogInfo("AgentEngine", $"[AGENT LOOP COMPLETE] No tools called. Final response generated at step {iteration}.");

                if (durableStateMachine is not null)
                {
                    if (!durableStateMachine.CanFinalize())
                    {
                        IReadOnlyList<AgentCompletionCriterion> pending = durableStateMachine.GetPendingRequiredCriteria();
                        string requirement = string.Join("; ", pending.Select(criterion => $"{criterion.Id}: {criterion.Description}"));
                        if (durableStateMachine.Snapshot.Phase == AgentRunPhase.Act)
                        {
                            await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Recover, "Completion was requested without required verification.", messages, cancellationToken);
                            await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Plan, "Plan the outstanding verification.", messages, cancellationToken);
                        }
                        messages.Add(new("user", $"[COMPLETION BLOCKED] Do not provide a final answer yet. The runtime has not observed successful verification for: {requirement}. Run the required verification command/tool and resolve failures before concluding."));
                        yield return new AgentStepEvent("state_changed", "Completion blocked until all required runtime verifications pass.", RunId: runId, Phase: durableStateMachine.Snapshot.Phase);
                        continue;
                    }

                    if (durableStateMachine.Snapshot.Phase == AgentRunPhase.Act)
                    {
                        await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Finalize, "All required completion criteria passed.", messages, cancellationToken);
                    }
                    durableStateMachine.SetOutcome(responseText, null, DateTimeOffset.UtcNow);
                    await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Completed, "Final response recorded.", messages, cancellationToken);
                    await AppendTraceAsync(runId, iteration, "run_completed", AgentRunPhase.Completed,
                        observation: responseText, outcome: "Completed", cancellationToken: cancellationToken);
                }

                if (episodicMemoryService != null && !string.IsNullOrWhiteSpace(request.Goal))
                {
                    try
                    {
                        string summary = responseText.Length > 300 ? $"{responseText[..300]}..." : responseText;
                        var mem = new OnlyRag.Core.AgentEpisodicMemory(
                            SessionId: $"session_{Guid.NewGuid():N}"[..12],
                            Goal: request.Goal,
                            Summary: summary,
                            KeyFacts: memoryManager.GetKeyFacts().ToList(),
                            Timestamp: DateTimeOffset.UtcNow);
                        await episodicMemoryService.SaveMemoryAsync(mem, CancellationToken.None);
                    }
                    catch { }
                }

                yield return new AgentStepEvent("final_response", responseText, RunId: runId, Phase: AgentRunPhase.Completed);
                yield break;
            }

            jsonRetryCount = 0;
            logger?.LogInfo("AgentEngine", $"[TOOLS PROPOSED] Extracted {toolCalls.Count} tool calls.");

            if (toolCalls.Count > 1)
            {
                yield return new AgentStepEvent("batch_tools_proposed", BatchToolCalls: toolCalls);
            }

            var resultsList = new List<AgentToolResult>();

            if (durableStateMachine is not null)
            {
                foreach (AgentToolCall _ in toolCalls)
                {
                    durableStateMachine.ConsumeToolCall(DateTimeOffset.UtcNow);
                }
                await SaveProgressAsync(durableStateMachine, messages, cancellationToken);
            }

            if (toolCalls.Count > 1 && toolCalls.All(AgentToolCallParser.IsReadOnlyTool))
            {
                logger?.LogInfo("AgentEngine", $"[PARALLEL TOOL EXECUTION] Concurrent execution of {toolCalls.Count} read-only tools.");
                yield return new AgentStepEvent("thought", $"[Agent Parallel Engine] Executing {toolCalls.Count} independent tools in parallel...");

                foreach (var tc in toolCalls)
                {
                    string parallelCallSig = $"{tc.ToolName}:{tc.ArgumentsJson.Trim()}";
                    recentToolSignatures.Add(parallelCallSig);
                    if (recentToolSignatures.Count > 30) recentToolSignatures.RemoveAt(0);

                    yield return new AgentStepEvent("tool_proposed", ToolCall: tc);
                }

                if (AgentCycleGuard.IsCyclicPatternDetected(recentToolSignatures))
                {
                    recentToolSignatures.Clear();
                    cycleGuardTriggerCount++;
                    if (cycleGuardTriggerCount >= 2)
                    {
                        logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TERMINATION] Reached maximum cycle guard warnings in parallel execution. Forcing loop completion.");
                        yield return new AgentStepEvent("thought", "[Agent Cycle Guard] Repeated cycle detected. Concluding agent loop with final summary.");
                        yield return new AgentStepEvent("final_response", "Agent execution paused after detecting repeated tool invocation patterns. Current findings have been saved.");
                        yield break;
                    }

                    logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TRIGGERED] Detected repetitive call cycle in parallel execution.");
                    yield return new AgentStepEvent("thought", "[Agent Cycle Guard] Detected repetitive call cycle in parallel execution. Injecting conclusion directive...");
                    messages.Add(new("user",
                        "[SYSTEM DIRECTIVE - STOP CYCLE] You have repeated the same calls multiple times without progress. " +
                        "Respond NOW to the user with the final summary in Markdown without invoking any more tools."));
                    continue;
                }

                var parallelTasks = toolCalls.Select(async tc =>
                {
                    string callSig = $"{tc.ToolName}:{tc.ArgumentsJson.Trim()}";
                    if (executedReadOnlyCallSignatures.Contains(callSig))
                    {
                        logger?.LogWarning("AgentEngine", $"[DUPLICATE GUARD] Tool in sola lettura già eseguito in precedenza: {callSig}");
                        return new AgentToolResult(
                            tc.CallId,
                            tc.ToolName,
                            true,
                            $"[SYSTEM NOTICE] Tool '{tc.ToolName}' with arguments '{tc.ArgumentsJson}' has already been executed and is available in working memory. Do not repeat the same call. Proceed to process the data or produce the final response.");
                    }

                    var res = await toolExecutor.ExecuteToolAsync(
                        tc.CallId,
                        tc.ToolName,
                        tc.ArgumentsJson,
                        workspaceRoot,
                        cancellationToken: cancellationToken);

                    if (res.Success) executedReadOnlyCallSignatures.Add(callSig);
                    return res;
                }).ToList();

                var parallelResults = await Task.WhenAll(parallelTasks);

                for (int parallelIndex = 0; parallelIndex < parallelResults.Length; parallelIndex++)
                {
                    AgentToolResult res = parallelResults[parallelIndex];
                    if (durableStateMachine is not null)
                    {
                        durableStateMachine.RecordVerification(toolCalls[parallelIndex], res, DateTimeOffset.UtcNow);
                        await AppendTraceAsync(runId, iteration, "tool_result", durableStateMachine.Snapshot.Phase,
                            toolCall: toolCalls[parallelIndex], result: res, evidence: res.Success ? res.Output : null, cancellationToken: cancellationToken);
                    }
                    yield return new AgentStepEvent("tool_result", ToolResult: res);
                    resultsList.Add(res);
                    if (res.Success && res.ToolName == "reflect_step")
                    {
                        memoryManager.AddKeyFact(res.Output);
                    }
                }
            }
            else
            {
                foreach (var toolCall in toolCalls)
                {
                    string callSignature = $"{toolCall.ToolName}:{toolCall.ArgumentsJson.Trim()}";

                    if (AgentToolCallParser.IsReadOnlyTool(toolCall) && executedReadOnlyCallSignatures.Contains(callSignature))
                    {
                        logger?.LogWarning("AgentEngine", $"[DUPLICATE GUARD TRIGGERED] Evitata chiamata ripetuta a tool in sola lettura: {callSignature}");
                        var cachedNotice = new AgentToolResult(
                            toolCall.CallId,
                            toolCall.ToolName,
                            true,
                            $"[SYSTEM NOTICE] Tool '{toolCall.ToolName}' with arguments '{toolCall.ArgumentsJson}' has already been executed successfully. The information is available in working memory. Do NOT repeat the call and proceed to the next step.");
                        yield return new AgentStepEvent("tool_result", ToolResult: cachedNotice);
                        resultsList.Add(cachedNotice);
                        continue;
                    }

                    if (failedToolSignatures.Contains(callSignature))
                    {
                        logger?.LogWarning("AgentEngine", $"[LOOP GUARD TRIGGERED] Chiamata ripetuta già fallita bloccata: {callSignature}");
                        failedToolSignatures.Remove(callSignature);
                        yield return new AgentStepEvent("thought", $"[Agent Loop Guard] Detected retry of a previously failed tool call ({toolCall.ToolName}). Sending correction instruction...");
                        messages.Add(new("user", $"[SYSTEM CORRECTION WARNING] Tool '{toolCall.ToolName}' with parameters '{toolCall.ArgumentsJson}' has ALREADY been executed and FAILED in the previous step. Do NOT repeat the same actions without modifying the parameters!"));
                        continue;
                    }

                    recentToolSignatures.Add(callSignature);
                    if (recentToolSignatures.Count > 30) recentToolSignatures.RemoveAt(0);

                    if (AgentCycleGuard.IsCyclicPatternDetected(recentToolSignatures))
                    {
                        recentToolSignatures.Clear();
                        cycleGuardTriggerCount++;
                        if (cycleGuardTriggerCount >= 2)
                        {
                            logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TERMINATION] Reached maximum cycle guard warnings. Forcing loop completion.");
                            yield return new AgentStepEvent("thought", "[Agent Cycle Guard] Repeated cycle detected. Concluding agent loop with final summary.");
                            yield return new AgentStepEvent("final_response", "Agent execution paused after detecting repeated tool invocation patterns. Current findings have been saved.");
                            yield break;
                        }

                        logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TRIGGERED] Detected repetitive call cycle: {callSignature}");
                        yield return new AgentStepEvent("thought", $"[Agent Cycle Guard] Detected repetitive action cycle ({toolCall.ToolName}). Injecting conclusion directive...");
                        messages.Add(new("user",
                            "[SYSTEM DIRECTIVE - STOP CYCLE] You have repeated the same calls multiple times without progress. " +
                            "Respond NOW to the user with the final summary in Markdown without invoking any more tools."));
                        continue;
                    }

                    bool needsApproval = toolCall.ToolName.Equals("run_command", StringComparison.OrdinalIgnoreCase) && !request.AutoApproveCommands;
                    var callWithApproval = toolCall with { RequiresApproval = needsApproval };

                    yield return new AgentStepEvent("tool_proposed", ToolCall: callWithApproval);

                    if (needsApproval)
                    {
                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        pendingApprovals[toolCall.CallId] = (callWithApproval, tcs);

                        yield return new AgentStepEvent("approval_required", ToolCall: callWithApproval);

                        bool approved = false;
                        try
                        {
                            approved = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
                        }
                        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
                        {
                            approved = false;
                            logger?.LogWarning("AgentEngine", $"Timeout or cancellation while waiting for approval for {toolCall.CallId}");
                        }
                        finally
                        {
                            pendingApprovals.TryRemove(toolCall.CallId, out _);
                        }

                        if (!approved)
                        {
                            var deniedResult = new AgentToolResult(
                                toolCall.CallId,
                                toolCall.ToolName,
                                false,
                                string.Empty,
                                "Command execution DENIED by user or timed out.");

                            yield return new AgentStepEvent("tool_result", ToolResult: deniedResult);
                            messages.Add(new("user", $"[TOOL RESULT ({toolCall.ToolName})]\nSuccess: False\nError: Execution denied by user."));
                            continue;
                        }
                    }

                    WorkspaceSnapshotCheckpoint? checkpoint = null;
                    if (checkpointManager != null && !AgentToolCallParser.IsReadOnlyTool(toolCall))
                    {
                        var targetPaths = ExtractTargetPathsFromArguments(toolCall.ArgumentsJson);
                        if (targetPaths.Count > 0)
                        {
                            checkpoint = checkpointManager.CreateCheckpoint($"cp_{toolCall.CallId}", workspaceRoot, targetPaths);
                        }

                        logger?.LogInfo("AgentEngine", $"[DYNAMIC CRITIQUE & TOT EVALUATION] Evaluating action candidate '{toolCall.ToolName}' on target paths: [{string.Join(", ", targetPaths)}]");
                        yield return new AgentStepEvent("thought", $"[Tree-of-Thought Critique] Evaluating candidate code mutation '{toolCall.ToolName}' safety and dependency impacts before execution...");
                    }

                    mctsMachine?.ExpandAndNavigate(callSignature, checkpoint?.CheckpointId);

                    var subagentChannel = System.Threading.Channels.Channel.CreateUnbounded<AgentStepEvent>();

                    var toolTask = toolExecutor.ExecuteToolAsync(
                        toolCall.CallId,
                        toolCall.ToolName,
                        toolCall.ArgumentsJson,
                        workspaceRoot,
                        onStep: step => subagentChannel.Writer.TryWrite(step),
                        cancellationToken: cancellationToken);

                    while (!toolTask.IsCompleted || subagentChannel.Reader.TryRead(out _))
                    {
                        while (subagentChannel.Reader.TryRead(out var subagentStep))
                        {
                            yield return subagentStep;
                        }

                        if (!toolTask.IsCompleted)
                        {
                            await Task.WhenAny(toolTask, Task.Delay(50, cancellationToken));
                        }
                    }

                    var result = await toolTask;

                    if (durableStateMachine is not null)
                    {
                        durableStateMachine.RecordVerification(toolCall, result, DateTimeOffset.UtcNow);
                        await AppendTraceAsync(runId, iteration, "tool_result", durableStateMachine.Snapshot.Phase,
                            toolCall: toolCall, result: result, evidence: result.Success ? result.Output : null, cancellationToken: cancellationToken);
                    }

                    bool isCompilationError = !result.Success &&
                        (!string.IsNullOrEmpty(result.Error) && (result.Error.Contains("error CS", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("TS", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("Build failed", StringComparison.OrdinalIgnoreCase)));

                    mctsMachine?.EvaluateAndBackpropagateCurrent(result.Success, isCompilationError);

                    if (!result.Success && checkpoint != null && checkpointManager != null)
                    {
                        logger?.LogWarning("AgentEngine", $"[MCTS CHECKPOINT ROLLBACK] Tool '{toolCall.ToolName}' failed. Restoring workspace snapshot checkpoint '{checkpoint.CheckpointId}' and navigating to parent MCTS node.");
                        checkpointManager.RestoreCheckpoint(checkpoint);
                        mctsMachine?.NavigateToParent();
                        yield return new AgentStepEvent("thought", $"[MCTS ROLLBACK APPLIED] Reverted workspace snapshot '{checkpoint.CheckpointId}' after failed '{toolCall.ToolName}' execution. Active tree state returned to parent MCTS node '{mctsMachine?.CurrentActiveNode?.NodeId}'.");
                    }

                    yield return new AgentStepEvent("tool_result", ToolResult: result);
                    resultsList.Add(result);

                    if (result.Success)
                    {
                        cycleGuardTriggerCount = 0;

                        if (astGraphService != null && !string.IsNullOrWhiteSpace(workspaceRoot))
                        {
                            var paths = ExtractTargetPathsFromArguments(toolCall.ArgumentsJson);
                            foreach (var p in paths)
                            {
                                string ext = Path.GetExtension(p).ToLowerInvariant();
                                if (ext is ".cs" or ".ts" or ".tsx" or ".js")
                                {
                                    try
                                    {
                                        var graphRes = await astGraphService.AnalyzeWorkspaceDependenciesAsync(workspaceRoot, p, cancellationToken);
                                        if (graphRes != null)
                                        {
                                            memoryManager.RegisterAstSymbols(p, graphRes.DirectDependencies, graphRes.DependentFiles);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }


                        if (AgentToolCallParser.IsReadOnlyTool(toolCall))
                        {
                            executedReadOnlyCallSignatures.Add(callSignature);
                            try
                            {
                                using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
                                if (doc.RootElement.TryGetProperty("relativePath", out var rp) || doc.RootElement.TryGetProperty("path", out rp))
                                {
                                    string? pathStr = rp.GetString();
                                    if (!string.IsNullOrEmpty(pathStr)) memoryManager.RegisterExploredPath(pathStr);
                                }
                            }
                            catch { }
                        }

                        if (toolCall.ToolName == "plan_task")
                        {
                            yield return new AgentStepEvent("plan_update", PlanMarkdown: result.Output);
                            try
                            {
                                using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
                                memoryManager.UpdatePlanFromToolCall(doc.RootElement);
                            }
                            catch { }
                        }
                        else if (toolCall.ToolName == "reflect_step")
                        {
                            memoryManager.AddKeyFact(result.Output);
                            try
                            {
                                using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
                                string stepId = doc.RootElement.TryGetProperty("stepId", out var sid) ? sid.GetString() ?? "1" : "1";
                                string status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() ?? "completed" : "completed";
                                string learnings = doc.RootElement.TryGetProperty("learnings", out var lr) ? lr.GetString() ?? "" : "";
                                memoryManager.UpdateStepStatus(stepId, status, learnings);
                            }
                            catch { }
                        }
                        else if (toolCall.ToolName.Contains("write") || toolCall.ToolName.Contains("replace"))
                        {
                            failedToolSignatures.Clear();
                            executedReadOnlyCallSignatures.Clear();
                            try
                            {
                                using var doc = JsonDocument.Parse(toolCall.ArgumentsJson);
                                if (doc.RootElement.TryGetProperty("relativePath", out var rp) || doc.RootElement.TryGetProperty("path", out rp))
                                {
                                    string? pathStr = rp.GetString();
                                    if (!string.IsNullOrEmpty(pathStr)) memoryManager.RegisterModifiedFile(pathStr);
                                }
                            }
                            catch { }
                        }
                    }
                    else if (!string.IsNullOrEmpty(result.Error))
                    {
                        failedToolSignatures.Add(callSignature);
                    }
                }
            }

            var batchMsgSb = new StringBuilder();
            foreach (var res in resultsList)
            {
                batchMsgSb.AppendLine($"[TOOL RESULT ({res.ToolName})]");
                batchMsgSb.AppendLine($"Success: {res.Success}");
                batchMsgSb.AppendLine($"Output:\n{res.Output}");
                if (!string.IsNullOrWhiteSpace(res.DiffPatch))
                {
                    batchMsgSb.AppendLine($"\n[UNIFIED DIFF ISPEZIONE MODIFICA]:\n{res.DiffPatch}");
                }
                if (!res.Success && !string.IsNullOrEmpty(res.Error))
                {
                    batchMsgSb.AppendLine($"Error: {res.Error}");
                    batchMsgSb.AppendLine("\n[SYSTEM DIAGNOSTIC & MANDATORY ERROR EVALUATION]");
                    batchMsgSb.AppendLine($"The action '{res.ToolName}' encountered an error or non-zero exit status.");
                    batchMsgSb.AppendLine("CRITICAL REQUIREMENT: Evaluate and resolve this error immediately before continuing!");
                    batchMsgSb.AppendLine("1. Analyze the exact error message and stack trace above to identify the root cause.");
                    batchMsgSb.AppendLine("2. Inspect affected code files using read_file or grep_search to understand the issue.");
                    batchMsgSb.AppendLine("3. Apply a targeted code or environment fix to resolve the root cause.");
                    batchMsgSb.AppendLine("4. Re-run verification (e.g. dotnet build, npm test via run_command) to verify the fix before moving to next steps.");
                }
                batchMsgSb.AppendLine();
            }

            accumulatedToolResults.AddRange(resultsList);

            if (durableStateMachine is not null)
            {
                bool anyFailure = resultsList.Any(result => !result.Success);
                if (durableStateMachine.Snapshot.Phase == AgentRunPhase.Act)
                {
                    await TransitionAndPersistAsync(
                        durableStateMachine,
                        anyFailure ? AgentRunPhase.Recover : AgentRunPhase.Observe,
                        anyFailure ? "A tool failed and recovery is required." : "Tool observations were recorded.",
                        messages,
                        cancellationToken);
                }

                if (anyFailure && durableStateMachine.Snapshot.Phase == AgentRunPhase.Recover)
                {
                    await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Plan, "Recovery plan required.", messages, cancellationToken);
                }
                else if (!anyFailure && durableStateMachine.Snapshot.Phase == AgentRunPhase.Observe)
                {
                    await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Verify, "Observations require verification.", messages, cancellationToken);
                    await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Plan, "Verification completed; planning next action.", messages, cancellationToken);
                }
            }

            if (batchMsgSb.Length > 0)
            {
                string batchText = batchMsgSb.ToString().TrimEnd();
                if (batchText.Length > 8000)
                {
                    batchText = batchText[..7800] + "\n\n...[WORKING CONTEXT TRUNCATED FOR CONTEXT BUDGET SAFETY]...";
                }
                messages.Add(new("user", batchText));
            }

            if (durableStateMachine is not null)
            {
                await SaveProgressAsync(durableStateMachine, messages, cancellationToken);
            }
        }

        _ = skillAutoLearner?.ExtractAndSaveSkillAsync(request.Goal, accumulatedToolResults, cancellationToken);

        if (maxIterations > 0 && iteration >= maxIterations)
        {
            logger?.LogWarning("AgentEngine", $"[AGENT LOOP END] Reached maximum iteration limit of {maxIterations}.");
            if (durableStateMachine is not null)
            {
                durableStateMachine.SetOutcome(null, "Iteration budget exceeded.", DateTimeOffset.UtcNow);
                if (durableStateMachine.Snapshot.Phase is not (AgentRunPhase.Completed or AgentRunPhase.Failed or AgentRunPhase.Cancelled))
                {
                    await TransitionAndPersistAsync(durableStateMachine, AgentRunPhase.Failed, "Iteration budget exceeded.", messages, cancellationToken);
                }
                await AppendTraceAsync(runId, iteration, "run_failed", durableStateMachine.Snapshot.Phase,
                    error: "Iteration budget exceeded.", outcome: "Failed", cancellationToken: cancellationToken);
            }
            yield return new AgentStepEvent("final_response", $"Reached the maximum agent iteration limit ({maxIterations} steps).", RunId: runId, Phase: AgentRunPhase.Failed);
        }
    }

    private async Task TransitionAndPersistAsync(
        PersistentAgentRunStateMachine stateMachine,
        AgentRunPhase nextPhase,
        string reason,
        IReadOnlyList<OllamaChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (runStateRepository is null) return;
        AgentRunTransition transition = stateMachine.TransitionTo(nextPhase, reason, DateTimeOffset.UtcNow);
        stateMachine.ReplaceMessages([JsonSerializer.Serialize(messages)], DateTimeOffset.UtcNow);
        await runStateRepository.AppendTransitionAsync(transition, cancellationToken);
        await runStateRepository.SaveAsync(stateMachine.Snapshot, cancellationToken);
    }

    private async Task SaveProgressAsync(
        PersistentAgentRunStateMachine stateMachine,
        IReadOnlyList<OllamaChatMessage> messages,
        CancellationToken cancellationToken)
    {
        if (runStateRepository is null) return;
        stateMachine.ReplaceMessages([JsonSerializer.Serialize(messages)], DateTimeOffset.UtcNow);
        await runStateRepository.SaveAsync(stateMachine.Snapshot, cancellationToken);
    }

    private Task AppendTraceAsync(
        string runId,
        int step,
        string eventType,
        AgentRunPhase phase,
        string? decision = null,
        AgentToolCall? toolCall = null,
        AgentToolResult? result = null,
        string? observation = null,
        int? estimatedTokens = null,
        double? latencyMs = null,
        string? evidence = null,
        string? error = null,
        string? outcome = null,
        CancellationToken cancellationToken = default)
    {
        if (runStateRepository is null) return Task.CompletedTask;
        return runStateRepository.AppendTraceEventAsync(new AgentRunTraceEvent(
            0, runId, step, eventType, DateTimeOffset.UtcNow, phase, decision,
            toolCall?.ToolName, toolCall?.CallId, result?.Success, observation,
            error ?? result?.Error, estimatedTokens, null, latencyMs, evidence, outcome), cancellationToken);
    }

    private static IReadOnlyList<AgentCompletionCriterion> NormalizeCompletionCriteria(IReadOnlyList<AgentCompletionCriterion>? criteria)
    {
        if (criteria is null || criteria.Count == 0)
        {
            return
            [
                new AgentCompletionCriterion(
                    "automated-verification",
                    "Run a successful build, test, lint, typecheck, or release gate command relevant to the goal.",
                    AgentCompletionVerificationKind.Command,
                    "run_command")
            ];
        }

        var normalized = new List<AgentCompletionCriterion>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AgentCompletionCriterion criterion in criteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.Id) || string.IsNullOrWhiteSpace(criterion.Description) || string.IsNullOrWhiteSpace(criterion.ExpectedToolName))
            {
                throw new ArgumentException("Each completion criterion requires an id, description, and expected tool name.");
            }
            if (!ids.Add(criterion.Id)) throw new ArgumentException($"Duplicate completion criterion id '{criterion.Id}'.");
            if (criterion.VerificationKind == AgentCompletionVerificationKind.Command && !string.Equals(criterion.ExpectedToolName, "run_command", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Command completion criterion '{criterion.Id}' must use run_command.");
            }
            normalized.Add(criterion with { Id = criterion.Id.Trim(), Description = criterion.Description.Trim(), ExpectedToolName = criterion.ExpectedToolName.Trim(), ExpectedCommand = criterion.ExpectedCommand?.Trim() });
        }
        return normalized;
    }

    internal static AgentToolCall? TryExtractToolCall(string text, ILoggingService? logger = null)
    {
        return AgentToolCallParser.TryExtractToolCall(text, logger);
    }

    internal static List<AgentToolCall> TryExtractToolCalls(string text, ILoggingService? logger = null)
    {
        return AgentToolCallParser.TryExtractToolCalls(text, logger);
    }

    internal static string EnrichGoalWithWorkspaceContext(string baseGoal, string workspaceRoot)
    {
        return AgentWorkspaceContextEnricher.EnrichGoalWithWorkspaceContext(baseGoal, workspaceRoot);
    }

    internal static bool IsCyclicPatternDetected(List<string> toolHistorySignature)
    {
        return AgentCycleGuard.IsCyclicPatternDetected(toolHistorySignature);
    }

    internal static bool IsReadOnlyTool(AgentToolCall call)
    {
        return AgentToolCallParser.IsReadOnlyTool(call);
    }

    private async Task<string> ResolveModelAsync(string? requestedModel, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel)) return requestedModel.Trim();
        OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.DefaultCodingModel)) return settings.DefaultCodingModel.Trim();
        if (!string.IsNullOrWhiteSpace(settings.DefaultChatModel)) return settings.DefaultChatModel.Trim();
        return "qwen2.5-coder";
    }

    private static string GetSystemPrompt(string? mode, bool autoApprove)
    {
        bool isWriteMode = string.Equals(mode, "write", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(mode);
        string modeLabel = isWriteMode ? "WRITE" : "ASK";
        string modeDescription = isWriteMode
            ? "Full agentic mode: you can read files, write files, execute commands, search the web, and modify the codebase."
            : "Read-only RAG mode: you can read files, search code, query the retrieval index, and inspect git state. Do NOT write or execute commands.";
        string approvalNote = autoApprove
            ? "Command auto-approval is ENABLED. run_command executes immediately without user confirmation."
            : "Command auto-approval is DISABLED. run_command requires explicit user approval before execution.";

        return $$"""
            You are OnlyRag Autonomous Agent (SOTA Edition) — an expert software development agent optimized for local LLMs (Qwen, Llama, DeepSeek, Mistral, Phi, Gemma).

            Operating mode: {{modeLabel}} — {{modeDescription}}
            {{approvalNote}}

            ## Persistent execution state machine

            The runtime owns the lifecycle: PLAN → ACT → OBSERVE → VERIFY → PLAN,
            with RECOVER on failure and FINALIZE → COMPLETED only when work is done.
            You propose a plan or action; never claim an action, verification, or completion
            that has not been observed by the runtime. State, budgets, and conversation
            history are persisted and can be resumed after a restart.

            ## Available Tools

            | Tool | Arguments | Description |
            |---|---|---|
            | list_dir | relativePath | List files and folders |
            | read_file | relativePath, startLine?, endLine? | Read file content with optional line range |
            | write_file | relativePath, content | Create or overwrite a file |
            | replace_file_content | relativePath, targetContent, replacementContent | Replace an exact block in a file |
            | multi_replace_file_content | relativePath, chunks[{targetContent, replacementContent}] | Multiple non-contiguous replacements in a specific file (relativePath must be a single file path) |
            | apply_diff_patch | relativePath?, patch | Apply standard Unified Diff patch (git diff) |
            | grep_search | query, searchPath | Fast code search (ripgrep) |
            | git_diff_inspect | relativePath? | Git status and diff |
            | run_command | commandLine, isAsync? | Execute PowerShell 7 command |
            | web_search | query, domain? | DuckDuckGo web search |
            | ingest_office_doc | relativePath, forceOcr? | RAG ingestion of Office/PDF documents |
            | generate_image_onnx | prompt, aspectRatio? | ONNX DirectML image generation |
            | query_retrieval_index | query, topK? | Hybrid FTS5+Qdrant RAG search |
            | plan_task | steps[{description}] | Create or update a dynamic plan checklist |
            | reflect_step | stepId, status, learnings | Record key facts after a completed action |
            | manage_task | action, taskId? | List/status/kill background tasks |
            | ast_structural_refactor | operation, targetSymbol, newSymbolName?, relativePath | AST-level symbol refactoring |
            | invoke_subagent | role, prompt, subagents?[{role, prompt}] | Spawn specialized sub-agents to execute sub-tasks concurrently |

            ## Tool Call Format

            Always respond with a valid JSON block. You may batch multiple independent read-only tools in a single array for parallel execution:

            **Single tool call:**
            ```json
            {
              "tool": "read_file",
              "arguments": { "relativePath": "src/Program.cs", "startLine": 1, "endLine": 50 },
              "explanation": "Reading entry point to understand app structure"
            }
            ```

            **Parallel tool calls (read-only tools only):**
            ```json
            [
              { "tool": "list_dir", "arguments": { "relativePath": "src" }, "explanation": "Explore source structure" },
              { "tool": "read_file", "arguments": { "relativePath": "README.md" }, "explanation": "Read project overview" },
              { "tool": "grep_search", "arguments": { "query": "public interface", "searchPath": "src" }, "explanation": "Find interfaces" }
            ]
            ```

            ## Behavioral Directives

            1. **ALWAYS plan before writing.** In WRITE mode, call plan_task at the start with an explicit step list before modifying any files.
            2. **ALWAYS verify after writing.** After changing code files, run `dotnet build` or `npm test` to catch errors before producing the final answer.
            3. **NEVER repeat a failed tool call** with the same arguments. Change strategy or parameters.
            4. **BATCH parallel reads.** Read multiple files simultaneously when they are independent.
            5. **Use reflect_step** after completing each planned step to record what you learned.
            6. **Final answer format.** When the goal is complete, respond with a clean Markdown summary: what changed, files affected, and verification results. Do NOT call any more tools after the final answer.
            7. **Project context awareness.** If AGENTS.md or PROJECT_STATUS.json exist at the workspace root, read them first and follow their conventions.
            8. **Workspace safety.** Never write outside the authorized workspace root. Never print secrets, keys, or credentials.
            9. **INTERNAL CLI EXECUTION MANDATE.** NEVER emit file-opening GUI commands (`start`, `open`, `explorer`, `code`, `notepad`, `Invoke-Item`). Execute all builds, tests, scripts, and commands internally via `run_command` (e.g. `dotnet build`, `npm test`, `pwsh .\\scripts\\...`).
            10. **MANDATORY ERROR EVALUATION & RESOLUTION.** When a command or tool call fails or produces an error/exception, you MUST immediately evaluate the error output, identify the root cause, apply a resolution, and re-verify the fix before continuing to subsequent task steps.
            """;
    }

    private static List<string> ExtractTargetPathsFromArguments(string argumentsJson)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(argumentsJson)) return paths;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("relativePath", out var rp) || doc.RootElement.TryGetProperty("path", out rp) || doc.RootElement.TryGetProperty("targetFile", out rp))
            {
                string? p = rp.GetString();
                if (!string.IsNullOrWhiteSpace(p)) paths.Add(p);
            }
        }
        catch { }
        return paths;
    }
}

