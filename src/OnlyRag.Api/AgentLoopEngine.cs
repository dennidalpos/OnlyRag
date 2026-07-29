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

        string enrichedGoal = EnrichGoalWithWorkspaceContext(request.Goal, workspaceRoot);

        var messages = new List<OllamaChatMessage>
        {
            new("system", GetSystemPrompt(request.Mode, request.AutoApproveCommands)),
            new("user", enrichedGoal)
        };

        yield return new AgentStepEvent("thought", $"[Agent Engine] Inizio elaborazione dell'obiettivo in modalità '{request.Mode}' con il modello '{model}'. Caricamento del modello ed elaborazione in corso...");

        int maxIterations = (request.MaxIterations.HasValue && request.MaxIterations.Value > 0) ? request.MaxIterations.Value : DefaultMaxIterations;
        var failedToolSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recentToolSignatures = new List<string>();
        int iteration = 0;
        int jsonRetryCount = 0;
        while (iteration < maxIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();

            PruneMessageHistoryIfNeeded(messages, logger);

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
            string numCtxLabel = $"{numCtx} tokens";
            string iterLabel = $"{iteration}/{maxIterations}";
            logger?.LogTrace("AgentEngine", $"[AGENT ITERATION {iterLabel}] Invio richiesta a Ollama (Model: {model}, NumCtx: {numCtxLabel}, Messages: {messages.Count})");

            yield return new AgentStepEvent("thought", $"[Agent Step {iterLabel}] Generazione risposta LLM e analisi azioni necessarie...");

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
                            if (hasMore)
                            {
                                chunk = enumerator.Current;
                            }
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
            logger?.LogTrace("AgentEngine", $"[LLM RESPONSE] Iteration {iteration}, Length: {responseText.Length} chars");

            if (string.IsNullOrWhiteSpace(responseText))
            {
                string errEmpty = "Il modello LLM non ha restituito alcun contenuto nella risposta.";
                logger?.LogWarning("AgentEngine", errEmpty);
                yield return new AgentStepEvent("error", errEmpty);
                yield break;
            }

            messages.Add(new("assistant", responseText));

            var toolCall = TryExtractToolCall(responseText, logger);
            if (toolCall == null)
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
                        "con tutte le chiavi e valori stringa racchiusi tra virgolette doppie. Non usare apici singoli né chiavi senza virgolette."));
                    continue;
                }

                logger?.LogInfo("AgentEngine", $"[AGENT LOOP COMPLETE] Nessun tool chiamato. Risposta finale generata al passo {iteration}.");
                yield return new AgentStepEvent("final_response", responseText);
                yield break;
            }

            jsonRetryCount = 0;
            logger?.LogInfo("AgentEngine", $"[TOOL PROPOSED] Tool: '{toolCall.ToolName}', CallId: '{toolCall.CallId}'");

            // Loop Guard: rileva chiamate identiche precedentemente fallite
            string callSignature = $"{toolCall.ToolName}:{toolCall.ArgumentsJson.Trim()}";
            if (failedToolSignatures.Contains(callSignature))
            {
                logger?.LogWarning("AgentEngine", $"[LOOP GUARD TRIGGERED] Chiamata ripetuta già fallita bloccata: {callSignature}");
                yield return new AgentStepEvent("thought", $"[Agent Loop Guard] Rilevato tentativo di rieseguire una chiamata di strumento già fallita ({toolCall.ToolName}). Invio istruzione di correzione...");
                string correctionPrompt = toolCall.ToolName.Equals("replace_file_content", StringComparison.OrdinalIgnoreCase)
                    ? $"[SYSTEM CORRECTION WARNING] Il tool 'replace_file_content' con parametri '{toolCall.ArgumentsJson}' è GIÀ STATO ESEGUITO ed è FALLITO perché TargetContent non è stato trovato nel file. NON ripetere la stessa stringa! Esegui prima read_file per verificare le righe esatte oppure usa write_file per riscrivere il file completo."
                    : $"[SYSTEM CORRECTION WARNING] Il tool '{toolCall.ToolName}' con parametri '{toolCall.ArgumentsJson}' è GIÀ STATO ESEGUITO ed è FALLITO al passo precedente. NON ripetere percorsi o comandi non esistenti!";
                messages.Add(new("user", correctionPrompt));
                continue;
            }

            // Cycle Guard: rileva ripetizioni cicliche o ripetitive di chiamate riuscite
            recentToolSignatures.Add(callSignature);
            if (recentToolSignatures.Count > 30)
            {
                recentToolSignatures.RemoveAt(0);
            }

            if (IsCyclicPatternDetected(recentToolSignatures))
            {
                logger?.LogWarning("AgentEngine", $"[CYCLE GUARD TRIGGERED] Rilevato ciclo di chiamate ripetitive: {callSignature}");
                yield return new AgentStepEvent("thought", $"[Agent Cycle Guard] Rilevato ciclo di azioni ripetitive ({toolCall.ToolName}). Iniezione direttiva di conclusione...");
                messages.Add(new("user",
                    "[DIRETTIVA SISTEMA - STOP CICLO] Hai ripetuto le stesse chiamate di strumento per più volte senza avanzamenti significativi. " +
                    "NON ripetere le stesse azioni! Se hai raccolto le informazioni o applicato le modifiche necessarie, rispondi ORA all'utente con il riepilogo finale in Markdown senza invocare altri tool."));
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

            string toolResultMsg = $"[TOOL RESULT ({toolCall.ToolName})]\nSuccesso: {result.Success}\nOutput:\n{result.Output}";
            if (!string.IsNullOrEmpty(result.Error))
            {
                failedToolSignatures.Add(callSignature);
                toolResultMsg += $"\nErrore: {result.Error}";
                if (result.Error.Contains("File non trovato", StringComparison.OrdinalIgnoreCase) || result.Error.Contains("Cartella non trovata", StringComparison.OrdinalIgnoreCase))
                {
                    toolResultMsg += "\n[SUGGERIMENTO SISTEMA] Il percorso specificato NON ESISTE sul disco! NON ipotizzare prefissi o sotto-cartelle (es. 'src/.../Views/...') se non sono state elencate da list_dir. Utilizza grep_search per cercare la posizione esatta del file oppure esegui list_dir con relativePath: \".\" per esplorare il workspace.";
                }
                else
                {
                    toolResultMsg += "\n[SUGGERIMENTO SISTEMA] L'operazione ha restituito un errore. Esamina attentamente la struttura del workspace ed i parametri prima di ritentare.";
                }
            }

            messages.Add(new("user", toolResultMsg));
        }

        if (maxIterations > 0 && iteration >= maxIterations)
        {
            logger?.LogWarning("AgentEngine", $"[AGENT LOOP END] Raggiunto limite massimo di {maxIterations} iterazioni.");
            yield return new AgentStepEvent("final_response", $"Raggiunto il limite massimo di iterazioni dell'agente ({maxIterations} passi).");
        }
    }

    internal static AgentToolCall? TryExtractToolCall(string text, ILoggingService? logger = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Cerca tag XML (<tool_call>, <tool>, <function_call>)
        var matchTagBlock = Regex.Match(text, @"<(?:tool_call|tool|function_call)>\s*(\{[\s\S]*?\})\s*</(?:tool_call|tool|function_call)>", RegexOptions.Singleline);
        if (matchTagBlock.Success)
        {
            var call = ParseToolCallJson(matchTagBlock.Groups[1].Value, logger);
            if (call != null) return call;
        }

        // 2. Cerca il blocco di codice markdown ```json ... ``` o ``` ... ```
        var matchCodeBlock = Regex.Match(text, @"```(?:json|JSON)?\s*(\{[\s\S]*?\})\s*(?:```|$)", RegexOptions.Singleline);
        if (matchCodeBlock.Success)
        {
            var call = ParseToolCallJson(matchCodeBlock.Groups[1].Value, logger);
            if (call != null) return call;
        }

        // 3. Bilanciamento delle graffe per JSON nidificati senza recinzioni markdown
        int firstBrace = text.IndexOf('{');
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
                string preview = jsonCandidate.Substring(0, Math.Min(120, jsonCandidate.Length)).ToLowerInvariant();
                if (preview.Contains("\"tool\"") || preview.Contains("\"tool_name\"") || preview.Contains("\"function\"") || preview.Contains("\"action\"") || preview.Contains("\"name\""))
                {
                    var call = ParseToolCallJson(jsonCandidate, logger);
                    if (call != null) return call;
                }
            }
        }

        if (text.Contains("\"tool\"") || text.Contains("\"tool_name\"") || text.Contains("\"action\"") || text.Contains("<tool_call>") || text.Contains("<tool>"))
        {
            logger?.LogTrace("AgentEngine", "Testo LLM contiene chiavi di tool ma l'estrazione JSON non ha restituito una chiamata valida.");
        }

        return null;
    }

    private static AgentToolCall? ParseToolCallJson(string json, ILoggingService? logger = null)
    {
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json, options);
        }
        catch (JsonException ex)
        {
            logger?.LogTrace("AgentEngine", $"Tentativo di parsing JSON standard fallito ({ex.Message}). Avvio riparazione tollerante JSON...");
            string repairedJson = RepairMalformedJson(json);
            try
            {
                doc = JsonDocument.Parse(repairedJson, options);
            }
            catch (Exception repairEx)
            {
                logger?.LogTrace("AgentEngine", $"JSON non parsabile neanche dopo riparazione: {repairEx.Message}");
            }
        }

        if (doc == null) return null;

        using (doc)
        {
            var root = doc.RootElement;

            // Supporto per formato OpenAI: {"function": {"name": "...", "arguments": {...}}} oppure {"type": "function", "function": ...}
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

            if (!string.IsNullOrWhiteSpace(toolRaw))
            {
                string normalizedTool = NormalizeToolName(toolRaw);
                string argsJson = "{}";

                JsonElement? argsElem = null;
                if (targetElement.TryGetProperty("arguments", out var argsProp)) argsElem = argsProp;
                else if (targetElement.TryGetProperty("args", out var aProp)) argsElem = aProp;
                else if (targetElement.TryGetProperty("parameters", out var pProp)) argsElem = pProp;
                else if (targetElement.TryGetProperty("inputs", out var iProp)) argsElem = iProp;
                else if (targetElement.TryGetProperty("input", out var inProp)) argsElem = inProp;
                else if (root.TryGetProperty("arguments", out var rootArgsProp)) argsElem = rootArgsProp;
                else if (root.TryGetProperty("parameters", out var rootParamProp)) argsElem = rootParamProp;

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
        }

        return null;
    }

    private static string RepairMalformedJson(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return rawJson;

        string repaired = rawJson;

        // 1. Ripara tabulazioni (0x09) ed a capo non-escaped all'interno di stringhe JSON
        repaired = FixUnescapedControlCharsInJsonStrings(repaired);

        // 2. Ripara a capo unescaped per campi noti
        repaired = FixUnescapedStringLiterals(repaired);

        // 3. Ripara backslash unescaped nei percorsi Windows e nelle stringhe (es. \M, \P, \O, \S, \C, \W, \U che non siano \n \r \t \\ \" \/)
        repaired = Regex.Replace(repaired, @"\\(?![\\""/bfnrt]|u[0-9a-fA-F]{4})", @"\\");

        // 4. Sostituisce apici singoli con virgolette doppie per chiavi e valori stringa
        repaired = Regex.Replace(repaired, @"'([^'\\]*(?:\\.[^'\\]*)*?)'", "\"$1\"");

        // 5. Aggiunge virgolette doppie alle chiavi unquoted (es. path: -> "path":)
        repaired = Regex.Replace(repaired, @"(?<=[{\s,])([a-zA-Z_][a-zA-Z0-9_]*)\s*:", "\"$1\":");

        // 6. Rimuove trailing commas prima di } o ] (es. {"a": 1,} -> {"a": 1})
        repaired = Regex.Replace(repaired, @",\s*([}\]])", "$1");

        // 7. Sostituisce valori Python-style con equivalenti JSON
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
                if (isEscaped)
                {
                    sb.Append(c);
                    isEscaped = false;
                }
                else if (c == '\\')
                {
                    sb.Append(c);
                    isEscaped = true;
                }
                else if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                }
                else if (c == '\t')
                {
                    sb.Append("\\t");
                }
                else if (c == '\r')
                {
                    sb.Append("\\r");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inString = true;
                }
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

        // Verifica se il testo contiene indicatori tipici di una tool call non parsata
        string lower = text.ToLowerInvariant();
        bool hasToolKeyword = lower.Contains("\"tool\"") || lower.Contains("\"tool_name\"") ||
                              lower.Contains("\"action\"") || lower.Contains("\"function\"") ||
                              lower.Contains("'tool'") || lower.Contains("'tool_name'") ||
                              lower.Contains("<tool_call>") || lower.Contains("<tool>");
        bool hasArgumentsKeyword = lower.Contains("\"arguments\"") || lower.Contains("\"args\"") ||
                                   lower.Contains("\"parameters\"") || lower.Contains("\"inputs\"") ||
                                   lower.Contains("'arguments'") || lower.Contains("'args'");
        bool hasJsonBlock = text.Contains("```json") || text.Contains("```JSON") ||
                            text.Contains("<tool_call>") || text.Contains("<tool>") ||
                            (text.Contains('{') && text.Contains('}'));

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
            "query_retrieval" or "query_retrieval_index" or "search_retrieval" or "vector_search" => "query_retrieval_index",
            "subagent" or "invoke_subagent" or "sub_agent" => "invoke_subagent",
            "task" or "manage_task" => "manage_task",
            _ => t
        };
    }

    private async Task<string> ResolveModelAsync(string? requestedModel, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.DefaultCodingModel))
        {
            return settings.DefaultCodingModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settings.DefaultChatModel))
        {
            return settings.DefaultChatModel.Trim();
        }

        return "qwen2.5-coder";
    }

    private static string GetSystemPrompt(string? mode, bool autoApprove)
    {
        string currentMode = mode ?? "write";
        return $$"""
Sei Antigravity Code Agent, un assistente agentico di sviluppo software di livello esperto compatibile con qualsiasi modello LLM (Qwen, Llama, DeepSeek, Mistral, Phi, Gemma).
Modalità operativa: {{currentMode}}
Auto-Approvazione comandi: {{autoApprove}}

Hai accesso completo al workspace del progetto ed alle risorse locali. Operi secondo il ciclo ReAct (Reasoning + Acting + Observation).

STRUMENTI DISPONIBILI:
1. list_dir({"relativePath": "string"})
2. read_file({"relativePath": "string", "startLine": 1, "endLine": 100}) [Alias: view_file]
3. write_file({"relativePath": "string", "content": "string"}) [Alias: write_to_file]
4. replace_file_content({"relativePath": "string", "targetContent": "string", "replacementContent": "string"})
5. multi_replace_file_content({"relativePath": "string", "chunks": [{"targetContent": "string", "replacementContent": "string"}]})
6. grep_search({"query": "string", "searchPath": "string"})
7. git_diff_inspect({"relativePath": "string"}) [Analisi dello stato Git e diff delle modifiche locale]
8. run_command({"commandLine": "string", "isAsync": false})
9. web_search({"query": "string", "domain": "string"}) [Ricerca online su documentazione e fonti ufficiali]
10. ingest_office_doc({"relativePath": "string", "forceOcr": false}) [Ingestion RAG di file Office/PDF tramite LibreOffice & PaddleOCR GPU]
11. generate_image_onnx({"prompt": "string", "aspectRatio": "1:1|16:9|9:16"}) [Generazione/editing immagini tramite ONNX DirectML GPU]
12. query_retrieval_index({"query": "string", "topK": 5}) [Interrogazione diretta indici SQLite FTS5 e Qdrant vectors]
13. manage_task({"action": "list|status|kill|send_input", "taskId": "string"})

REGOLE PER LE CHIAMATE DI STRUMENTO (UNIVERSAL LLM FORMAT):
Per invocare uno strumento, rispondi RIGOROSAMENTE con un blocco di codice JSON racchiuso in ```json ... ``` o con il tag <tool_call> ... </tool_call> nel formato:
```json
{
  "tool": "nome_tool",
  "arguments": {
    "relativePath": "."
  },
  "explanation": "Motivazione del passo..."
}
```

REGOLE COMPORTAMENTALI (ANTIGRAVITY DIRECTIVES):
1. **PRIMO PASSO OBBLIGATORIO**: Inizia SEMPRE con una chiamata a list_dir con relativePath "." per esplorare la struttura del workspace. Non rispondere MAI direttamente all'utente senza prima aver esplorato il progetto.
2. **Uso di Percorsi Reali**: Non ipotizzare mai percorsi o nomi di cartelle non esistenti. Esamina i file restituiti da list_dir o grep_search e usa ESATTAMENTE quei percorsi. Se un file/cartella non viene trovata, torna ad esplorare la radice con list_dir con relativePath: ".".
3. **Esplorazione Approfondita & Fonti Ufficiali**: Usa read_file e grep_search per il codice locale. Se necessiti di verificare documentazione o standard ufficiali, usa web_search con query mirate.
4. **MODALITÀ SCRITTURA (MODIFICA OBBLIGATORIA SU DISCO)**: Quando sei in modalità 'write' e l'utente ti chiede di creare, modificare o rifattorizzare codice, DEVI OBBLIGATORIAMENTE chiamare `write_file`, `replace_file_content` o `multi_replace_file_content` per applicare le modifiche direttamente sul disco prima di concludere! NON limitarti a stampare il codice nel testo finale senza aver invocato il tool di scrittura.
5. **Modalità Plan**: In modalità 'plan', NON chiamare mai write_file, replace_file_content, multi_replace_file_content o run_command. Concentrati sull'analisi architetturale e la pianificazione.
6. **Lavoro Diretto**: Esegui le operazioni direttamente con i tool forniti.
7. **Una Chiamata per Passo**: Esegui una sola chiamata di strumento per ciascuna risposta.
8. **Risposta Finale**: Soltanto DOPO aver eseguito i tool di scrittura necessari per completare l'obiettivo, fornisci la tua sintesi finale in Markdown normale SENZA blocchi JSON di strumenti.
""";
    }

    private static string EnrichGoalWithWorkspaceContext(string goal, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return goal;
        }

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

                string agentsPath = Path.Combine(workspaceRoot, "AGENTS.md");
                if (File.Exists(agentsPath))
                {
                    detectedItems.Add("- AGENTS.md (Istruzioni e convenzioni generali del repository)");
                }

                string statusPath = Path.Combine(workspaceRoot, "PROJECT_STATUS.json");
                if (File.Exists(statusPath))
                {
                    detectedItems.Add("- PROJECT_STATUS.json (Todo attivi e stato del progetto)");
                }

                string settingsPath = Path.Combine(workspaceRoot, "workspace_settings.json");
                if (File.Exists(settingsPath))
                {
                    detectedItems.Add("- workspace_settings.json (Impostazioni e switch attivi del workspace)");
                }

                string readmePath = Path.Combine(workspaceRoot, "README.md");
                if (File.Exists(readmePath))
                {
                    detectedItems.Add("- README.md (Panoramica e guida principale del repository)");
                }

                string skillsDir = Path.Combine(workspaceRoot, "skills");
                if (Directory.Exists(skillsDir))
                {
                    detectedItems.Add("- skills/ (Directory skill e linee guida di dominio del progetto)");
                }

                if (detectedItems.Count > 0)
                {
                    sb.AppendLine("File di contesto e configurazione identificati nella radice del progetto:");
                    foreach (var item in detectedItems)
                    {
                        sb.AppendLine(item);
                    }
                }

                if (File.Exists(settingsPath))
                {
                    try
                    {
                        string settingsJson = File.ReadAllText(settingsPath, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(settingsJson) && settingsJson.Length < 2000)
                        {
                            sb.AppendLine();
                            sb.AppendLine("Contenuto switch/configurazione di workspace_settings.json:");
                            sb.AppendLine(settingsJson.Trim());
                        }
                    }
                    catch
                    {
                        // Safe fallback
                    }
                }
            }
        }
        catch
        {
            // Safe fallback
        }

        sb.AppendLine();
        sb.AppendLine("ISTRUZIONI PER L'AGENTE:");
        sb.AppendLine("1. Esegui sempre list_dir con relativePath: \".\" al primo passo per esplorare l'albero dei file ed identificare i moduli.");
        sb.AppendLine("2. Se presenti, leggi prioritariamente AGENTS.md, PROJECT_STATUS.json o workspace_settings.json per rispettare le convenzioni del progetto, le responsabilita dei file e gli switch di configurazione.");

        return sb.ToString();
    }

    private static void PruneMessageHistoryIfNeeded(List<OllamaChatMessage> messages, ILoggingService? logger)
    {
        const int maxMessagesBeforePruning = 20;
        const int keepRecentCount = 12;

        if (messages.Count <= maxMessagesBeforePruning) return;

        int removeCount = messages.Count - 2 - keepRecentCount;
        if (removeCount <= 0) return;

        logger?.LogInfo("AgentEngine", $"[CONTEXT SLIDING WINDOW] Contesto troppo ampio ({messages.Count} messaggi). Troncamento di {removeCount} messaggi intermedi per ottimizzazione memoria LLM.");

        messages.RemoveRange(2, removeCount);
        messages.Insert(2, new OllamaChatMessage("system", "[CONTESTO COMPRESSO SISTEMA] La cronologia intermedia delle iterazioni precedenti è stata sinteticamente compressa per mantenere la finestra di contesto fluida ed evitare amnesie o loop. Prosegui dall'ultimo stato utile."));
    }

    private static bool IsCyclicPatternDetected(List<string> history)
    {
        if (history.Count < 4) return false;

        // 1. Stessa identica chiamata per 3 volte di seguito
        int n = history.Count;
        if (history[n - 1] == history[n - 2] && history[n - 2] == history[n - 3])
        {
            return true;
        }

        // 2. Pattern di 2 o 3 chiamate ripetuto 3 volte (es. A B A B A B oppure A B C A B C)
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
