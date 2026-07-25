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
    private const int MaxAgentIterations = 10;
    private const int MaxJsonRetries = 2;
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

        var failedToolSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int iteration = 0;
        int jsonRetryCount = 0;
        while (iteration < MaxAgentIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();

            OllamaSettings? settings = null;
            try
            {
                settings = await settingsService.GetAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("AgentEngine", $"Impossibile recuperare le impostazioni Ollama, uso context default: {ex.Message}");
            }

            int numCtx = settings?.CodingNumCtx ?? DefaultNumCtx;
            logger?.LogTrace("AgentEngine", $"[AGENT ITERATION {iteration}/{MaxAgentIterations}] Invio richiesta a Ollama (Model: {model}, NumCtx: {numCtx}, Messages: {messages.Count})");

            yield return new AgentStepEvent("thought", $"[Agent Step {iteration}/{MaxAgentIterations}] Generazione risposta LLM e analisi azioni necessarie...");

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

                        if (!hasMore) break;

                        if (!string.IsNullOrEmpty(chunk))
                        {
                            responseSb.Append(chunk);
                            yield return new AgentStepEvent("thought_chunk", Content: chunk);
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
                yield return new AgentStepEvent("thought", $"[Agent Loop Guard] Rilevato tentativo di rieseguire una chiamata di strumento già fallita ({toolCall.ToolName}). Invio istruzione per esplorare la radice del progetto...");
                messages.Add(new("user",
                    $"[SYSTEM CORRECTION WARNING] Il tool '{toolCall.ToolName}' con parametri '{toolCall.ArgumentsJson}' è GIÀ STATO ESEGUITO ed è FALLITO al passo precedente. " +
                    $"NON ripetere percorsi o comandi non esistenti! Inizia eseguendo list_dir con relativePath: \".\" per esaminare la struttura effettiva del workspace."));
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
                toolResultMsg += "\n[SUGGERIMENTO SISTEMA] L'operazione ha restituito un errore. Esamina attentamente la struttura dei file del progetto eseguendo list_dir con relativePath: \".\" prima di tentare altri percorsi.";
            }

            messages.Add(new("user", toolResultMsg));
        }

        logger?.LogWarning("AgentEngine", $"[AGENT LOOP END] Raggiunto limite massimo di {MaxAgentIterations} iterazioni.");
        yield return new AgentStepEvent("final_response", "Raggiunto il limite massimo di iterazioni dell'agente (10 passi).");
    }

    internal static AgentToolCall? TryExtractToolCall(string text, ILoggingService? logger = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Cerca tag <tool_call> ... </tool_call> (tipici di molti modelli open source come Qwen/DeepSeek/Llama)
        var matchTagBlock = Regex.Match(text, @"<tool_call>\s*(\{[\s\S]*?\})\s*</tool_call>", RegexOptions.Singleline);
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
                var call = ParseToolCallJson(jsonCandidate, logger);
                if (call != null) return call;
            }
        }

        if (text.Contains("\"tool\"") || text.Contains("\"tool_name\"") || text.Contains("\"action\"") || text.Contains("<tool_call>"))
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

            string? toolRaw = null;
            if (root.TryGetProperty("tool", out var toolProp)) toolRaw = toolProp.GetString();
            else if (root.TryGetProperty("tool_name", out var toolNameProp)) toolRaw = toolNameProp.GetString();
            else if (root.TryGetProperty("function", out var fnProp)) toolRaw = fnProp.GetString();
            else if (root.TryGetProperty("action", out var actProp)) toolRaw = actProp.GetString();
            else if (root.TryGetProperty("name", out var nameProp)) toolRaw = nameProp.GetString();

            if (!string.IsNullOrWhiteSpace(toolRaw))
            {
                string normalizedTool = NormalizeToolName(toolRaw);
                string argsJson = "{}";
                if (root.TryGetProperty("arguments", out var argsProp)) argsJson = argsProp.GetRawText();
                else if (root.TryGetProperty("args", out var aProp)) argsJson = aProp.GetRawText();
                else if (root.TryGetProperty("parameters", out var pProp)) argsJson = pProp.GetRawText();
                else if (root.TryGetProperty("inputs", out var iProp)) argsJson = iProp.GetRawText();
                else if (root.TryGetProperty("input", out var inProp)) argsJson = inProp.GetRawText();

                string? explanation = root.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;

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

        // 1. Rimuove commenti single-line (// ...) fuori dalle stringhe
        repaired = Regex.Replace(repaired, @"(?<!:)//[^\n]*", "");

        // 2. Rimuove commenti multi-line (/* ... */)
        repaired = Regex.Replace(repaired, @"/\*.*?\*/", "", RegexOptions.Singleline);

        // 3. Ripara a capo unescaped all'interno dei campi di codice stringa
        repaired = FixUnescapedStringLiterals(repaired);

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
                              lower.Contains("<tool_call>");
        bool hasArgumentsKeyword = lower.Contains("\"arguments\"") || lower.Contains("\"args\"") ||
                                   lower.Contains("\"parameters\"") || lower.Contains("\"inputs\"") ||
                                   lower.Contains("'arguments'") || lower.Contains("'args'");
        bool hasJsonBlock = text.Contains("```json") || text.Contains("```JSON") ||
                            text.Contains("<tool_call>") ||
                            (text.Contains('{') && text.Contains('}'));

        return hasToolKeyword && (hasArgumentsKeyword || hasJsonBlock);
    }

    private static string NormalizeToolName(string toolName)
    {
        string t = toolName.Trim().ToLowerInvariant();
        return t switch
        {
            "list" or "listdir" or "ls" or "list_directory" => "list_dir",
            "read" or "readfile" or "read_file_content" or "view_file" => "read_file",
            "write" or "writefile" or "create_file" or "write_to_file" => "write_file",
            "replace" or "replacefile" or "replace_content" => "replace_file_content",
            "grep" or "search" or "find" or "grep_search" => "grep_search",
            "run" or "exec" or "execute" or "command" or "terminal" or "run_command" => "run_command",
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
Sei Antigravity Code Agent, un assistente agentico di sviluppo software di livello esperto per Windows 10/11 (PowerShell 7).
Modalità operativa: {{currentMode}}
Auto-Approvazione comandi: {{autoApprove}}

Hai accesso completo al workspace del progetto ed alle risorse locali. Operi secondo il ciclo ReAct (Reasoning + Acting + Observation).

STRUMENTI DISPONIBILI:
1. list_dir({"relativePath": "string"})
2. read_file({"relativePath": "string", "startLine": 1, "endLine": 100}) [Alias: view_file]
3. write_file({"relativePath": "string", "content": "string"}) [Alias: write_to_file]
4. replace_file_content({"relativePath": "string", "targetContent": "string", "replacementContent": "string"})
5. grep_search({"query": "string", "searchPath": "string"})
6. run_command({"commandLine": "string", "isAsync": false})

REGOLE PER LE CHIAMATE DI STRUMENTO:
Per invocare uno strumento, rispondi RIGOROSAMENTE con un blocco di codice JSON racchiuso in ```json ... ``` con questo formato:
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
3. **Esplorazione Approfondita**: Usa read_file per leggere i file sorgente rilevanti e grep_search per cercare pattern specifici. Non fornire mai analisi senza aver letto il codice sorgente effettivo.
4. **Scrittura Precisa**: Usa replace_file_content per modifiche mirate a blocchi di testo, oppure write_file per creare nuovi file o riscrivere file completi.
5. **Modalità Plan**: In modalità 'plan', NON chiamare mai write_file, replace_file_content o run_command. Concentrati sull'analisi architetturale e la pianificazione.
6. **Lavoro Diretto**: Devi fare il lavoro tu stesso con i tool disponibili. NON delegare il lavoro a subagenti o tool inesistenti.
7. **Una Chiamata per Passo**: Esegui una sola chiamata di strumento per ciascuna risposta.
8. **Risposta Finale**: Quando l'obiettivo è completato, fornisci la tua sintesi finale in Markdown normale SENZA blocchi JSON di strumenti.
""";
    }

    private static string EnrichGoalWithWorkspaceContext(string goal, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return goal;
        }

        return $"""
{goal}

[CONTESTO WORKSPACE]
Il workspace del progetto è attivo alla cartella: {workspaceRoot}
Inizia esplorando la struttura del progetto con list_dir per comprendere il contesto prima di agire.
""";
    }
}
