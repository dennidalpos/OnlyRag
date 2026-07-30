using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;
using OnlyRag.Infrastructure.Logging;

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
    private readonly ILoggingService? logger;

    private readonly ConcurrentDictionary<string, (AgentToolCall Call, TaskCompletionSource<bool> Tcs)> pendingApprovals = new();

    public AgentLoopEngine(
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService,
        WorkspaceToolExecutor toolExecutor,
        BackgroundTaskManager taskManager,
        OnlyRag.Infrastructure.Agent.Memory.IAgentEpisodicMemoryService? episodicMemoryService = null,
        ILoggingService? logger = null)
    {
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
        this.toolExecutor = toolExecutor;
        this.taskManager = taskManager;
        this.episodicMemoryService = episodicMemoryService;
        this.logger = logger;
    }

    public bool ApproveToolCall(string callId, bool approved)
    {
        if (pendingApprovals.TryRemove(callId, out var pending))
        {
            pending.Tcs.TrySetResult(approved);
            logger?.LogInfo("AgentEngine", $"Approvazione tool call {callId}: {approved}");
            return true;
        }

        logger?.LogWarning("AgentEngine", $"Tentativo di approvazione per callId non trovata: {callId}");
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
            resolveError = $"Errore durante la risoluzione del modello LLM per l'agente: {ex.Message}";
            logger?.LogError("AgentEngine", resolveError, ex);
        }

        if (resolveError != null)
        {
            yield return new AgentStepEvent("error", resolveError);
            yield break;
        }

        var memoryManager = new AgentMemoryManager(logger);

        if (episodicMemoryService != null && !string.IsNullOrWhiteSpace(request.Goal))
        {
            try
            {
                var recalledMemories = await episodicMemoryService.SearchRelevantMemoriesAsync(request.Goal, topK: 3, cancellationToken);
                if (recalledMemories.Count > 0)
                {
                    memoryManager.AddRecalledMemories(recalledMemories);
                    logger?.LogInfo("AgentEngine", $"[EPISODIC MEMORY RECALL] Richiamate {recalledMemories.Count} memorie rilevanti da sessioni precedenti.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("AgentEngine", $"Impossibile richiamare le memorie episodiche: {ex.Message}");
            }
        }

        string enrichedGoal = EnrichGoalWithWorkspaceContext(request.Goal, workspaceRoot);

        var messages = new List<OllamaChatMessage>
        {
            new("system", GetSystemPrompt(request.Mode, request.AutoApproveCommands)),
            new("user", enrichedGoal)
        };

        yield return new AgentStepEvent("thought", $"[Agent Engine SOTA] Inizio elaborazione dell'obiettivo in modalità '{request.Mode}' con il modello '{model}'. Caricamento memoria ed esecuzione in corso...");

        int maxIterations = (request.MaxIterations.HasValue && request.MaxIterations.Value > 0) ? request.MaxIterations.Value : DefaultMaxIterations;
        var failedToolSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var executedReadOnlyCallSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentToolSignatures = new List<string>();
        int iteration = 0;
        int jsonRetryCount = 0;

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

            memoryManager.PruneHistory(messages);

            string iterLabel = $"{iteration}/{maxIterations}";
            logger?.LogTrace("AgentEngine", $"[AGENT ITERATION {iterLabel}] Sending request to Ollama (Model: {model}, NumCtx: {numCtx}, Messages: {messages.Count})");

            yield return new AgentStepEvent("thought", $"[Agent Step {iterLabel}] Generazione risposta LLM ed analisi ragionamento...");

            var responseSb = new StringBuilder();
            IAsyncEnumerator<string>? enumerator = null;
            string? streamError = null;
            try
            {
                enumerator = ollamaClient.GenerateChatStreamAsync(
                    model,
                    messages,
                    numCtx: numCtx,
                    cancellationToken: cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                streamError = $"Errore nell'interrogazione di Ollama con il modello '{model}': {ex.Message}";
                logger?.LogError("AgentEngine", streamError, ex);
            }

            if (streamError != null)
            {
                yield return new AgentStepEvent("error", streamError);
                yield break;
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
                            streamError = $"Errore durante lo streaming da Ollama con il modello '{model}': {ex.Message}";
                            logger?.LogError("AgentEngine", streamError, ex);
                            break;
                        }

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

            if (streamError != null)
            {
                yield return new AgentStepEvent("error", streamError);
                yield break;
            }

            string responseText = responseSb.ToString();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                string errEmpty = "Il modello LLM non ha restituito alcun contenuto nella risposta.";
                logger?.LogWarning("AgentEngine", errEmpty);
                yield return new AgentStepEvent("error", errEmpty);
                yield break;
            }

            messages.Add(new("assistant", responseText));

            var toolCalls = TryExtractToolCalls(responseText, logger);
            if (toolCalls.Count == 0)
            {
                if (LooksLikeFailedToolCall(responseText) && jsonRetryCount < MaxJsonRetries)
                {
                    jsonRetryCount++;
                    logger?.LogWarning("AgentEngine", $"[JSON PARSE RETRY {jsonRetryCount}/{MaxJsonRetries}] Rilevata tool call con JSON malformato, invio prompt correttivo.");
                    yield return new AgentStepEvent("json_parse_warning",
                        $"⚠️ JSON malformato nella risposta LLM (tentativo di correzione {jsonRetryCount}/{MaxJsonRetries}). Richiesta ripetizione al modello...");
                    messages.Add(new("user",
                        "[ERRORE DI FORMATO] La tua risposta precedente conteneva un tentativo di chiamata tool, " +
                        "ma il JSON non era valido e non è stato possibile parsarlo. " +
                        "Rispondi RIGOROSAMENTE con un unico blocco ```json { \"tool\": \"nome_tool\", \"arguments\": {...} } ``` " +
                        "oppure un array ```json [ {\"tool\": \"...\", \"arguments\": {...}} ] ``` per chiamate multiple in parallelo."));
                    continue;
                }

                logger?.LogInfo("AgentEngine", $"[AGENT LOOP COMPLETE] Nessun tool chiamato. Risposta finale generata al passo {iteration}.");

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

                yield return new AgentStepEvent("final_response", responseText);
                yield break;
            }

            jsonRetryCount = 0;
            logger?.LogInfo("AgentEngine", $"[TOOLS PROPOSED] Estratte {toolCalls.Count} chiamate di strumento.");

            if (toolCalls.Count > 1)
            {
                yield return new AgentStepEvent("batch_tools_proposed", BatchToolCalls: toolCalls);
            }

            var resultsList = new List<AgentToolResult>();

            if (toolCalls.Count > 1 && toolCalls.All(IsReadOnlyTool))
            {
                logger?.LogInfo("AgentEngine", $"[PARALLEL TOOL EXECUTION] Esecuzione concorrente di {toolCalls.Count} tool in sola lettura.");
                yield return new AgentStepEvent("thought", $"[Agent Parallel Engine] Esecuzione in parallelo di {toolCalls.Count} strumenti indipendenti...");

                foreach (var tc in toolCalls)
                {
                    string parallelCallSig = $"{tc.ToolName}:{tc.ArgumentsJson.Trim()}";
                    recentToolSignatures.Add(parallelCallSig);
                    if (recentToolSignatures.Count > 30) recentToolSignatures.RemoveAt(0);

                    yield return new AgentStepEvent("tool_proposed", ToolCall: tc);
                }

                if (IsCyclicPatternDetected(recentToolSignatures))
                {
                    logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TRIGGERED] Rilevato ciclo di chiamate ripetitive in esecuzione parallela.");
                    yield return new AgentStepEvent("thought", "[Agent Cycle Guard] Rilevato ciclo di azioni ripetitive. Iniezione direttiva di conclusione...");
                    messages.Add(new("user",
                        "[DIRETTIVA SISTEMA - STOP CICLO] Hai ripetuto le stesse chiamate per più volte senza avanzamenti. " +
                        "Rispondi ORA all'utente con il resoconto finale in Markdown senza invocare altri tool."));
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
                            $"[AVVISO SISTEMA] Il tool '{tc.ToolName}' con argomenti '{tc.ArgumentsJson}' è già stato eseguito ed è già disponibile nella memoria di lavoro. Non ripetere la stessa chiamata. Procedi ad elaborare i dati o la risposta finale.");
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

                foreach (var res in parallelResults)
                {
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

                    if (IsReadOnlyTool(toolCall) && executedReadOnlyCallSignatures.Contains(callSignature))
                    {
                        logger?.LogWarning("AgentEngine", $"[DUPLICATE GUARD TRIGGERED] Evitata chiamata ripetuta a tool in sola lettura: {callSignature}");
                        var cachedNotice = new AgentToolResult(
                            toolCall.CallId,
                            toolCall.ToolName,
                            true,
                            $"[AVVISO SISTEMA] Il tool '{toolCall.ToolName}' con argomenti '{toolCall.ArgumentsJson}' è già stato eseguito ed ha avuto successo. Le informazioni sono già disponibili nella memoria di lavoro. NON ripetere la chiamata e procedi con il passo successivo.");
                        yield return new AgentStepEvent("tool_result", ToolResult: cachedNotice);
                        resultsList.Add(cachedNotice);
                        continue;
                    }

                    if (failedToolSignatures.Contains(callSignature))
                    {
                        logger?.LogWarning("AgentEngine", $"[LOOP GUARD TRIGGERED] Chiamata ripetuta già fallita bloccata: {callSignature}");
                        failedToolSignatures.Remove(callSignature);
                        yield return new AgentStepEvent("thought", $"[Agent Loop Guard] Rilevato tentativo di rieseguire una chiamata di strumento già fallita ({toolCall.ToolName}). Invio istruzione di correzione...");
                        messages.Add(new("user", $"[SYSTEM CORRECTION WARNING] Il tool '{toolCall.ToolName}' con parametri '{toolCall.ArgumentsJson}' è GIÀ STATO ESEGUITO ed è FALLITO al passo precedente. NON ripetere le stesse azioni senza modificare i parametri!"));
                        continue;
                    }

                    recentToolSignatures.Add(callSignature);
                    if (recentToolSignatures.Count > 30) recentToolSignatures.RemoveAt(0);

                    if (IsCyclicPatternDetected(recentToolSignatures))
                    {
                        logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TRIGGERED] Rilevato ciclo di chiamate ripetitive: {callSignature}");
                        yield return new AgentStepEvent("thought", $"[Agent Cycle Guard] Rilevato ciclo di azioni ripetitive ({toolCall.ToolName}). Iniezione direttiva di conclusione...");
                        messages.Add(new("user",
                            "[DIRETTIVA SISTEMA - STOP CICLO] Hai ripetuto le stesse chiamate per più volte senza avanzamenti. " +
                            "Rispondi ORA all'utente con il resoconto finale in Markdown senza invocare altri tool."));
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
                            logger?.LogWarning("AgentEngine", $"Timeout o cancellazione durante l'attesa di approvazione per {toolCall.CallId}");
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
                                "Esecuzione del comando RIFIUTATA dall'utente o andata in timeout.");

                            yield return new AgentStepEvent("tool_result", ToolResult: deniedResult);
                            messages.Add(new("user", $"[TOOL RESULT ({toolCall.ToolName})]\nSuccesso: False\nErrore: Esecuzione rifiutata dall'utente."));
                            continue;
                        }
                    }

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

                    yield return new AgentStepEvent("tool_result", ToolResult: result);
                    resultsList.Add(result);

                    if (result.Success)
                    {
                        if (IsReadOnlyTool(toolCall))
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
                        }
                        else if (toolCall.ToolName == "reflect_step")
                        {
                            memoryManager.AddKeyFact(result.Output);
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
                batchMsgSb.AppendLine($"Successo: {res.Success}");
                batchMsgSb.AppendLine($"Output:\n{res.Output}");
                if (!res.Success && !string.IsNullOrEmpty(res.Error))
                {
                    batchMsgSb.AppendLine($"Errore: {res.Error}");
                }
                batchMsgSb.AppendLine();
            }

            if (batchMsgSb.Length > 0)
            {
                messages.Add(new("user", batchMsgSb.ToString().TrimEnd()));
            }
        }

        if (maxIterations > 0 && iteration >= maxIterations)
        {
            logger?.LogWarning("AgentEngine", $"[AGENT LOOP END] Raggiunto limite massimo di {maxIterations} iterazioni.");
            yield return new AgentStepEvent("final_response", $"Raggiunto il limite massimo di iterazioni dell'agente ({maxIterations} passi).");
        }
    }

    internal static AgentToolCall? TryExtractToolCall(string text, ILoggingService? logger = null)
    {
        var calls = TryExtractToolCalls(text, logger);
        return calls.Count > 0 ? calls[0] : null;
    }

    internal static List<AgentToolCall> TryExtractToolCalls(string text, ILoggingService? logger = null)
    {
        var list = new List<AgentToolCall>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        // 1. Cerca tag XML (<tool_call>, <tool>, <function_call>)
        var matchTagBlock = Regex.Match(text, @"<(?:tool_call|tool|function_call)>\s*([\s\S]*?)\s*</(?:tool_call|tool|function_call)>", RegexOptions.Singleline);
        if (matchTagBlock.Success)
        {
            ParseAndAddCalls(matchTagBlock.Groups[1].Value, list, logger);
            if (list.Count > 0) return list;
        }

        // 2. Cerca il blocco di codice markdown ```json ... ``` o ``` ... ```
        var matchCodeBlock = Regex.Match(text, @"```(?:json|JSON)?\s*([\s\S]*?)\s*(?:```|$)", RegexOptions.Singleline);
        if (matchCodeBlock.Success)
        {
            ParseAndAddCalls(matchCodeBlock.Groups[1].Value, list, logger);
            if (list.Count > 0) return list;
        }

        // 3. Bilanciamento graffe/quadre per JSON non recintati
        int firstBrace = text.IndexOf('{');
        int firstBracket = text.IndexOf('[');

        if (firstBracket != -1 && (firstBrace == -1 || firstBracket < firstBrace))
        {
            int lastBracket = text.LastIndexOf(']');
            if (lastBracket > firstBracket)
            {
                string jsonCandidate = text.Substring(firstBracket, lastBracket - firstBracket + 1);
                ParseAndAddCalls(jsonCandidate, list, logger);
                if (list.Count > 0) return list;
            }
        }

        if (firstBrace != -1)
        {
            int openCount = 0;
            int lastBrace = -1;
            for (int i = firstBrace; i < text.Length; i++)
            {
                if (text[i] == '{') openCount++;
                else if (text[i] == '}')
                {
                    openCount--;
                    if (openCount == 0)
                    {
                        lastBrace = i;
                        break;
                    }
                }
            }

            if (lastBrace > firstBrace)
            {
                string jsonCandidate = text.Substring(firstBrace, lastBrace - firstBrace + 1);
                ParseAndAddCalls(jsonCandidate, list, logger);
                if (list.Count > 0) return list;
            }
        }

        return list;
    }

    private static void ParseAndAddCalls(string rawJson, List<AgentToolCall> targetList, ILoggingService? logger)
    {
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        string sanitizedJson = FixUnescapedControlCharsInJsonStrings(rawJson);
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(sanitizedJson, options);
        }
        catch
        {
            string repairedJson = RepairMalformedJson(sanitizedJson);
            try { doc = JsonDocument.Parse(repairedJson, options); }
            catch { }
        }

        if (doc == null) return;

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var call = ExtractSingleCallFromElement(item);
                    if (call != null) targetList.Add(call);
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                var call = ExtractSingleCallFromElement(root);
                if (call != null) targetList.Add(call);
            }
        }
    }

    private static AgentToolCall? ExtractSingleCallFromElement(JsonElement root)
    {
        JsonElement targetElement = root;
        if (root.TryGetProperty("function", out var fnObj) && fnObj.ValueKind == JsonValueKind.Object)
        {
            targetElement = fnObj;
        }

        string? toolRaw = null;
        if (targetElement.TryGetProperty("tool", out var toolProp)) toolRaw = toolProp.GetString();
        else if (targetElement.TryGetProperty("tool_name", out var toolNameProp)) toolRaw = toolNameProp.GetString();
        else if (targetElement.TryGetProperty("function", out var fnProp) && fnProp.ValueKind == JsonValueKind.String) toolRaw = fnProp.GetString();
        else if (targetElement.TryGetProperty("action", out var actProp)) toolRaw = actProp.GetString();
        else if (targetElement.TryGetProperty("name", out var nameProp)) toolRaw = nameProp.GetString();

        if (string.IsNullOrWhiteSpace(toolRaw)) return null;

        string normalizedTool = NormalizeToolName(toolRaw);
        string argsJson = "{}";

        JsonElement? argsElem = null;
        if (targetElement.TryGetProperty("arguments", out var argsProp)) argsElem = argsProp;
        else if (targetElement.TryGetProperty("args", out var aProp)) argsElem = aProp;
        else if (targetElement.TryGetProperty("parameters", out var pProp)) argsElem = pProp;
        else if (targetElement.TryGetProperty("inputs", out var iProp)) argsElem = iProp;
        else if (root.TryGetProperty("arguments", out var rootArgsProp)) argsElem = rootArgsProp;

        if (argsElem.HasValue)
        {
            if (argsElem.Value.ValueKind == JsonValueKind.String)
            {
                string strVal = argsElem.Value.GetString() ?? "{}";
                argsJson = strVal.Trim().StartsWith('{') ? strVal : "{ \"input\": " + JsonSerializer.Serialize(strVal) + " }";
            }
            else
            {
                argsJson = argsElem.Value.GetRawText();
            }
        }

        string? explanation = root.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(explanation) && targetElement.TryGetProperty("explanation", out var expProp2))
        {
            explanation = expProp2.GetString();
        }

        return new AgentToolCall(
            CallId: $"call_{Guid.NewGuid():N}"[..10],
            ToolName: normalizedTool,
            ArgumentsJson: argsJson,
            Explanation: explanation);
    }

    private static string RepairMalformedJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;
        string repaired = FixUnescapedControlCharsInJsonStrings(rawJson);
        repaired = FixUnescapedStringLiterals(repaired);
        repaired = Regex.Replace(repaired, @"\\(?![\\""/bfnrt]|u[0-9a-fA-F]{4})", @"\\");
        repaired = Regex.Replace(repaired, @"'([^'\\]*(?:\\.[^'\\]*)*?)'", "\"$1\"");
        repaired = Regex.Replace(repaired, @"(?<=[{\s,])([a-zA-Z_][a-zA-Z0-9_]*)\s*:", "\"$1\":");
        repaired = Regex.Replace(repaired, @",\s*([}\]])", "$1");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)True(?=\s*[,}\]])", "true");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)False(?=\s*[,}\]])", "false");
        repaired = Regex.Replace(repaired, @"(?<=:\s*)None(?=\s*[,}\]])", "null");
        return repaired;
    }

    private static string FixUnescapedControlCharsInJsonStrings(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        var sb = new StringBuilder(json.Length + 64);
        bool inString = false;
        bool isEscaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString)
            {
                if (isEscaped) { sb.Append(c); isEscaped = false; }
                else if (c == '\\') { sb.Append(c); isEscaped = true; }
                else if (c == '"') { sb.Append(c); inString = false; }
                else if (c == '\t') sb.Append("\\t");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\n') sb.Append("\\n");
                else sb.Append(c);
            }
            else
            {
                if (c == '"') inString = true;
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static string FixUnescapedStringLiterals(string text)
    {
        return Regex.Replace(text, @"(?<=""(?:content|replacementContent|targetContent|query|commandLine)"":\s*"")([\s\S]*?)(?=""\s*[,}])", m =>
        {
            return m.Value.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n").Replace("\t", "\\t");
        });
    }

    private static bool LooksLikeFailedToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string lower = text.ToLowerInvariant();
        bool hasToolKeyword = lower.Contains("\"tool\"") || lower.Contains("\"tool_name\"") ||
                              lower.Contains("\"action\"") || lower.Contains("\"function\"") ||
                              lower.Contains("<tool_call>") || lower.Contains("<tool>");
        bool hasArgumentsKeyword = lower.Contains("\"arguments\"") || lower.Contains("\"args\"") ||
                                   lower.Contains("\"parameters\"") || lower.Contains("\"inputs\"");
        bool hasJsonBlock = text.Contains("```json") || text.Contains("<tool_call>") || (text.Contains('{') && text.Contains('}'));

        return hasToolKeyword && (hasArgumentsKeyword || hasJsonBlock);
    }

    private static string NormalizeToolName(string toolName)
    {
        string t = toolName.Trim().ToLowerInvariant();
        return t switch
        {
            "list" or "listdir" or "ls" or "dir" or "list_directory" or "list_files" => "list_dir",
            "read" or "readfile" or "read_file_content" or "view_file" or "cat" => "read_file",
            "write" or "writefile" or "create_file" or "create" or "write_to_file" => "write_file",
            "replace" or "replacefile" or "replace_content" or "edit" or "edit_file" => "replace_file_content",
            "multi_replace" or "multi_replace_file_content" or "batch_replace" => "multi_replace_file_content",
            "grep" or "search" or "find" or "grep_search" or "find_in_files" => "grep_search",
            "git_diff" or "git_status" or "git_diff_inspect" or "git" => "git_diff_inspect",
            "run" or "exec" or "execute" or "command" or "terminal" or "run_command" or "cmd" or "powershell" => "run_command",
            "web_search" or "search_web" or "internet_search" or "online_search" or "ddg" or "google" => "web_search",
            "ingest_office" or "ingest_office_doc" or "office_ingest" or "ingest_document" => "ingest_office_doc",
            "generate_image" or "generate_image_onnx" or "image_gen" or "create_image" => "generate_image_onnx",
            "query_retrieval" or "query_retrieval_index" or "search_retrieval" or "vector_search" or "rag_hybrid_search" or "rag_search" => "query_retrieval_index",
            "plan" or "plan_task" or "create_plan" or "update_plan" => "plan_task",
            "reflect" or "reflect_step" or "self_reflection" => "reflect_step",
            "subagent" or "invoke_subagent" or "sub_agent" => "invoke_subagent",
            "task" or "manage_task" => "manage_task",
            _ => t
        };
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
            : "Read-only RAG mode: you can read files, search code, query the retrieval index, and inspect git state. Do NOT write or execute commands."
        ;
        string approvalNote = autoApprove
            ? "Command auto-approval is ENABLED. run_command executes immediately without user confirmation."
            : "Command auto-approval is DISABLED. run_command requires explicit user approval before execution.";

        return $$"""
            You are OnlyRag Autonomous Agent (SOTA Edition) — an expert software development agent optimized for local LLMs (Qwen, Llama, DeepSeek, Mistral, Phi, Gemma).

            Operating mode: {{modeLabel}} — {{modeDescription}}
            {{approvalNote}}

            ## ReAct Reasoning Cycle

            Follow this strict cycle for every action:
            1. THINK — Reason about the goal, current state, and what information you need.
            2. ACT — Call one or more tools using the JSON format below.
            3. OBSERVE — Read the tool results carefully.
            4. REFLECT — Update your understanding; register key facts with reflect_step.
            5. REPEAT or ANSWER — Continue until the goal is fully achieved, then produce a final Markdown summary.

            ## Available Tools

            | Tool | Arguments | Description |
            |---|---|---|
            | list_dir | relativePath | List files and folders |
            | read_file | relativePath, startLine?, endLine? | Read file content with optional line range |
            | write_file | relativePath, content | Create or overwrite a file |
            | replace_file_content | relativePath, targetContent, replacementContent | Replace an exact block in a file |
            | multi_replace_file_content | relativePath, chunks[{targetContent, replacementContent}] | Multiple non-contiguous replacements |
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
            """;
    }

    private static string EnrichGoalWithWorkspaceContext(string goal, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return goal;

        var sb = new StringBuilder();
        sb.AppendLine(goal);
        sb.AppendLine();
        sb.AppendLine("[CONTESTO WORKSPACE ATTIVO]");
        sb.AppendLine($"Cartella radice del progetto: {workspaceRoot}");

        try
        {
            if (Directory.Exists(workspaceRoot))
            {
                var detectedItems = new List<string>();
                if (File.Exists(Path.Combine(workspaceRoot, "AGENTS.md"))) detectedItems.Add("- AGENTS.md (Istruzioni e convenzioni generali del repository)");
                if (File.Exists(Path.Combine(workspaceRoot, "PROJECT_STATUS.json"))) detectedItems.Add("- PROJECT_STATUS.json (Todo attivi e stato del progetto)");
                if (File.Exists(Path.Combine(workspaceRoot, "workspace_settings.json"))) detectedItems.Add("- workspace_settings.json (Impostazioni e switch attivi del workspace)");
                if (File.Exists(Path.Combine(workspaceRoot, "README.md"))) detectedItems.Add("- README.md (Panoramica e guida principale del repository)");
                if (Directory.Exists(Path.Combine(workspaceRoot, "skills"))) detectedItems.Add("- skills/ (Directory skill e linee guida di dominio)");

                if (detectedItems.Count > 0)
                {
                    sb.AppendLine("File di contesto e configurazione identificati nella radice:");
                    foreach (var item in detectedItems) sb.AppendLine(item);
                }

                string statusPath = Path.Combine(workspaceRoot, "PROJECT_STATUS.json");
                if (File.Exists(statusPath))
                {
                    try
                    {
                        string statusJson = File.ReadAllText(statusPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(statusJson) && statusJson.Length < 3000)
                        {
                            sb.AppendLine("\nContenuto attuale di PROJECT_STATUS.json:");
                            sb.AppendLine(statusJson.Trim());
                        }
                    }
                    catch { }
                }

                string settingsPath = Path.Combine(workspaceRoot, "workspace_settings.json");
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string settingsJson = File.ReadAllText(settingsPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(settingsJson) && settingsJson.Length < 2000)
                        {
                            sb.AppendLine("\nContenuto switch/configurazione di workspace_settings.json:");
                            sb.AppendLine(settingsJson.Trim());
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        sb.AppendLine("\nISTRUZIONI PER L'AGENTE:");
        sb.AppendLine("1. Esplora i file e la struttura del progetto solo se strettamente necessario. Se la struttura è già nota o fornita nel contesto, non rieseguire list_dir e procedi direttamente con il task.");
        sb.AppendLine("2. Se presenti, leggi e rispetta prioritariamente AGENTS.md e PROJECT_STATUS.json.");

        return sb.ToString();
    }

    private static bool IsCyclicPatternDetected(List<string> history)
    {
        if (history.Count < 3) return false;
        int n = history.Count;

        string GetToolName(string sig)
        {
            int idx = sig.IndexOf(':');
            return idx > 0 ? sig[..idx].Trim().ToLowerInvariant() : sig.Trim().ToLowerInvariant();
        }

        string t1 = GetToolName(history[n - 1]);
        string t2 = GetToolName(history[n - 2]);
        string t3 = GetToolName(history[n - 3]);

        if (t1 is "reflect_step" or "plan_task" && t2 is "reflect_step" or "plan_task" && t3 is "reflect_step" or "plan_task")
        {
            return true;
        }

        if (history.Count >= 4)
        {
            if (history[n - 1] == history[n - 2] && history[n - 2] == history[n - 3]) return true;

            for (int period = 2; period <= 4; period++)
            {
                if (n >= period * 3)
                {
                    bool matchExact = true;
                    bool matchToolName = true;

                    for (int i = 0; i < period; i++)
                    {
                        string elem = history[n - 1 - i];
                        string name = GetToolName(elem);

                        if (history[n - 1 - i - period] != elem || history[n - 1 - i - (period * 2)] != elem)
                        {
                            matchExact = false;
                        }

                        if (GetToolName(history[n - 1 - i - period]) != name || GetToolName(history[n - 1 - i - (period * 2)]) != name)
                        {
                            matchToolName = false;
                        }
                    }

                    if (matchExact || matchToolName) return true;
                }
            }
        }

        return false;
    }

    private static bool IsReadOnlyTool(AgentToolCall call)
    {
        // Only tools that are truly stateless and idempotent qualify for parallel execution.
        // plan_task and reflect_step are excluded: they mutate agent state (plan checklist, key-facts store).
        string t = call.ToolName.ToLowerInvariant();
        return t is "read_file" or "list_dir" or "grep_search" or "git_diff_inspect" or "web_search" or "query_retrieval_index";
    }
}

