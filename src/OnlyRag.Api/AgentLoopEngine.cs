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
    private readonly ILoggingService? logger;

    private readonly ConcurrentDictionary<string, (AgentToolCall Call, TaskCompletionSource<bool> Tcs)> pendingApprovals = new();

    public AgentLoopEngine(
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService,
        WorkspaceToolExecutor toolExecutor,
        BackgroundTaskManager taskManager,
        ILoggingService? logger = null)
    {
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
        this.toolExecutor = toolExecutor;
        this.taskManager = taskManager;
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
        string enrichedGoal = EnrichGoalWithWorkspaceContext(request.Goal, workspaceRoot);

        var messages = new List<OllamaChatMessage>
        {
            new("system", GetSystemPrompt(request.Mode, request.AutoApproveCommands)),
            new("user", enrichedGoal)
        };

        yield return new AgentStepEvent("thought", $"[Agent Engine SOTA] Inizio elaborazione dell'obiettivo in modalità '{request.Mode}' con il modello '{model}'. Caricamento memoria ed esecuzione in corso...");

        int maxIterations = (request.MaxIterations.HasValue && request.MaxIterations.Value > 0) ? request.MaxIterations.Value : DefaultMaxIterations;
        var failedToolSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentToolSignatures = new List<string>();
        int iteration = 0;
        int jsonRetryCount = 0;

        while (iteration < maxIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();

            memoryManager.PruneHistory(messages);

            OllamaSettings? settings = null;
            try
            {
                settings = await settingsService.GetAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("AgentEngine", $"Impossibile recuperare le impostazioni Ollama, uso context default: {ex.Message}");
            }

            int numCtx = settings?.CodingNumCtx ?? settings?.ChatNumCtx ?? DefaultNumCtx;
            string iterLabel = $"{iteration}/{maxIterations}";
            logger?.LogTrace("AgentEngine", $"[AGENT ITERATION {iterLabel}] Invio richiesta a Ollama (Model: {model}, NumCtx: {numCtx}, Messages: {messages.Count})");

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

            foreach (var toolCall in toolCalls)
            {
                string callSignature = $"{toolCall.ToolName}:{toolCall.ArgumentsJson.Trim()}";
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

                var result = await toolExecutor.ExecuteToolAsync(
                    toolCall.CallId,
                    toolCall.ToolName,
                    toolCall.ArgumentsJson,
                    workspaceRoot,
                    cancellationToken);

                yield return new AgentStepEvent("tool_result", ToolResult: result);
                resultsList.Add(result);

                if (result.Success)
                {
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
        string currentMode = mode ?? "write";
        return $$"""
Sei Antigravity Autonomous Code Agent (SOTA Edition), un assistente agentico di sviluppo software di livello esperto compatibile con i principali LLM (Qwen, Llama, DeepSeek, Mistral, Phi, Gemma).
Modalità operativa: {{currentMode}}
Auto-Approvazione comandi: {{autoApprove}}

Operi secondo un rigoroso ciclo ReAct SOTA (Plan -> Multi-Tool Execute -> Auto-Verify -> Reflect):

STRUMENTI DISPONIBILI:
1. list_dir({"relativePath": "string"}) [Elenco file e cartelle]
2. read_file({"relativePath": "string", "startLine": 1, "endLine": 100}) [Alias: view_file - Lettura sicura file]
3. write_file({"relativePath": "string", "content": "string"}) [Alias: write_to_file - Scrittura/creazione file]
4. replace_file_content({"relativePath": "string", "targetContent": "string", "replacementContent": "string"}) [Sostituzione mirata di blocchi di codice]
5. multi_replace_file_content({"relativePath": "string", "chunks": [{"targetContent": "string", "replacementContent": "string"}]}) [Sostituzione multipla non contigua]
6. grep_search({"query": "string", "searchPath": "string"}) [Ricerca ripgrep nel codice]
7. git_diff_inspect({"relativePath": "string"}) [Analisi dello stato Git e diff delle modifiche locale]
8. run_command({"commandLine": "string", "isAsync": false}) [Esecuzione comandi di build, test e terminale]
9. web_search({"query": "string", "domain": "string"}) [Ricerca online su documentazione e fonti ufficiali]
10. ingest_office_doc({"relativePath": "string", "forceOcr": false}) [Ingestion RAG di file Office/PDF tramite LibreOffice & PaddleOCR GPU]
11. generate_image_onnx({"prompt": "string", "aspectRatio": "1:1|16:9|9:16"}) [Generazione/editing immagini tramite ONNX DirectML GPU]
12. query_retrieval_index({"query": "string", "topK": 5}) [Alias: rag_hybrid_search - Interrogazione diretta indici SQLite FTS5 e Qdrant vectors]
13. plan_task({"steps": [{"description": "string"}]}) [Pianificazione dinamica e checklist del lavoro dell'agente]
14. reflect_step({"stepId": "string", "status": "completed|failed", "learnings": "string"}) [Self-reflection post-azione e memorizzazione di fatto chiave]
15. invoke_subagent({"prompt": "string", "role": "string"}) [Delega di sotto-task ad agenti autonomi secondari]
16. manage_task({"action": "list|status|kill|send_input", "taskId": "string"}) [Gestione processi ed agenti in background]
17. ast_structural_refactor({"operation": "rename_symbol|find_references|replace_symbol_body", "targetSymbol": "string", "newSymbolName": "string", "relativePath": "string"}) [Rifattorizzazione e ricerca simboli AST]

FORMATO RISPOSTA MULTI-TOOL (JSON):
Puoi restituire una o più chiamate di strumento nello stesso blocco JSON per eseguire letture ed ispezioni in parallelo:
```json
[
  {
    "tool": "read_file",
    "arguments": { "relativePath": "src/OnlyRag.Api/AgentLoopEngine.cs" },
    "explanation": "Lettura file di ragionamento"
  },
  {
    "tool": "grep_search",
    "arguments": { "query": "AgentStepEvent", "searchPath": "src/OnlyRag.Core" },
    "explanation": "Ispezione contratti DTO"
  }
]
```

REGOLE COMPORTAMENTALI (SOTA DIRECTIVES):
1. **PLANNING**: Prima di apportare modifiche in modalità 'write', definisci mentalmente la sequenza dei passi e verifica l'esistenza dei file con list_dir o grep_search.
2. **ESECUZIONE E VERIFICA AUTOMATICA**: Dopo aver modificato file di codice, esegui SEMPRE `run_command` (`dotnet build`, `npm test`) per verificare che non vi siano errori di sintassi o compilazione prima di terminare.
3. **RISPOSTA FINALE**: Soltanto dopo aver verificato le modifiche, fornisci il resoconto finale in Markdown pulito.
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
        sb.AppendLine("1. Esegui sempre list_dir con relativePath: \".\" al primo passo per esplorare l'albero dei file.");
        sb.AppendLine("2. Se presenti, leggi e rispetta prioritariamente AGENTS.md e PROJECT_STATUS.json.");

        return sb.ToString();
    }

    private static bool IsCyclicPatternDetected(List<string> history)
    {
        if (history.Count < 4) return false;
        int n = history.Count;
        if (history[n - 1] == history[n - 2] && history[n - 2] == history[n - 3]) return true;

        for (int period = 2; period <= 4; period++)
        {
            if (n >= period * 3)
            {
                bool match = true;
                for (int i = 0; i < period; i++)
                {
                    string elem = history[n - 1 - i];
                    if (history[n - 1 - i - period] != elem || history[n - 1 - i - (period * 2)] != elem)
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
        }

        return false;
    }
}
