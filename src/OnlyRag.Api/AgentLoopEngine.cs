using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent;

namespace OnlyRag.Api;

internal sealed class AgentLoopEngine
{
    private const int MaxAgentIterations = 10;
    private const int DefaultNumCtx = 16384;

    private readonly IOllamaClient ollamaClient;
    private readonly IOllamaSettingsService settingsService;
    private readonly WorkspaceToolExecutor toolExecutor;
    private readonly BackgroundTaskManager taskManager;

    private readonly ConcurrentDictionary<string, (AgentToolCall Call, TaskCompletionSource<bool> Tcs)> pendingApprovals = new();

    public AgentLoopEngine(
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService,
        WorkspaceToolExecutor toolExecutor,
        BackgroundTaskManager taskManager)
    {
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
        this.toolExecutor = toolExecutor;
        this.taskManager = taskManager;
    }

    public bool ApproveToolCall(string callId, bool approved)
    {
        if (pendingApprovals.TryRemove(callId, out var pending))
        {
            pending.Tcs.TrySetResult(approved);
            return true;
        }

        return false;
    }

    public async IAsyncEnumerable<AgentStepEvent> RunAgentLoopAsync(
        AgentRunRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string model = await ResolveModelAsync(request.Model, cancellationToken);
        string workspaceRoot = request.WorkspaceRoot ?? "";

        var messages = new List<OllamaChatMessage>
        {
            new("system", GetSystemPrompt(request.Mode, request.AutoApproveCommands))
        };

        messages.Add(new("user", request.Goal));

        yield return new AgentStepEvent("thought", $"[Agent Engine] Inizio elaborazione obiettivo in modalità '{request.Mode}' con modello '{model}'...");

        int iteration = 0;
        while (iteration < MaxAgentIterations)
        {
            iteration++;
            cancellationToken.ThrowIfCancellationRequested();

            OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
            int numCtx = settings.CodingNumCtx ?? DefaultNumCtx;

            yield return new AgentStepEvent("thought", $"[Agent Step {iteration}/{MaxAgentIterations}] Elaborazione del modello in corso...");

            var responseSb = new StringBuilder();
            await foreach (string chunk in ollamaClient.GenerateChatStreamAsync(
                model,
                messages,
                numCtx: numCtx,
                cancellationToken: cancellationToken))
            {
                responseSb.Append(chunk);
                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return new AgentStepEvent("thought_chunk", Content: chunk);
                }
            }

            string responseText = responseSb.ToString();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                yield return new AgentStepEvent("error", "Il modello non ha restituito alcun contenuto.");
                yield break;
            }

            messages.Add(new("assistant", responseText));

            var toolCall = TryExtractToolCall(responseText);
            if (toolCall == null)
            {
                // Nessuna chiamata a tool rilevata: l'agente ha concluso o fornito la risposta finale
                yield return new AgentStepEvent("final_response", responseText);
                yield break;
            }

            // Notifica della chiamata a tool proposta
            bool needsApproval = toolCall.ToolName.Equals("run_command", StringComparison.OrdinalIgnoreCase) && !request.AutoApproveCommands;
            var callWithApproval = toolCall with { RequiresApproval = needsApproval };

            yield return new AgentStepEvent("tool_proposed", ToolCall: callWithApproval);

            if (needsApproval)
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                pendingApprovals[toolCall.CallId] = (callWithApproval, tcs);

                yield return new AgentStepEvent("approval_required", ToolCall: callWithApproval);

                // Attende la risposta dell'utente via UI o timeout (60s)
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                bool approved = false;
                try
                {
                    approved = await tcs.Task.WaitAsync(linkedCts.Token);
                }
                catch
                {
                    approved = false;
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

            // Esegue il tool
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
                toolResultMsg += $"\nErrore: {result.Error}";
            }

            messages.Add(new("user", toolResultMsg));
        }

        yield return new AgentStepEvent("final_response", "Raggiunto il limite massimo di iterazioni dell'agente (10 passi).");
    }

    internal static AgentToolCall? TryExtractToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 1. Cerca il primo blocco di codice markdown ```json ... ``` o ```JSON ... ``` (anche non chiuso se troncato)
        var matchCodeBlock = Regex.Match(text, @"```(?:json|JSON)?\s*(\{[\s\S]*?\})\s*(?:```|$)", RegexOptions.Singleline);
        if (matchCodeBlock.Success)
        {
            var call = ParseToolCallJson(matchCodeBlock.Groups[1].Value);
            if (call != null) return call;
        }

        // 2. Cerca qualsiasi oggetto JSON contenente la chiave "tool", "function" o "action"
        var matchRawJson = Regex.Match(text, @"\{[^{}]*""(?:tool|tool_name|function|action|name)""\s*:\s*""[^""]+""[\s\S]*?\}", RegexOptions.Singleline);
        if (matchRawJson.Success)
        {
            var call = ParseToolCallJson(matchRawJson.Value);
            if (call != null) return call;
        }

        // 3. Fallback: bilanciamento graffe dal primo '{' al corrispettivo '}'
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
                var call = ParseToolCallJson(jsonCandidate);
                if (call != null) return call;
            }
        }

        return null;
    }

    private static AgentToolCall? ParseToolCallJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
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
                else argsJson = json; // Fallback to entire object as args if top-level properties

                string? explanation = root.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;

                return new AgentToolCall(
                    CallId: $"call_{Guid.NewGuid():N}"[..10],
                    ToolName: normalizedTool,
                    ArgumentsJson: argsJson,
                    Explanation: explanation);
            }
        }
        catch
        {
            // Ignora JSON non valido
        }

        return null;
    }

    private static string NormalizeToolName(string toolName)
    {
        string t = toolName.Trim().ToLowerInvariant();
        return t switch
        {
            "list" or "listdir" or "ls" or "list_directory" => "list_dir",
            "read" or "readfile" or "read_file_content" => "read_file",
            "write" or "writefile" or "create_file" => "write_file",
            "replace" or "replacefile" or "replace_content" or "edit_file" => "replace_file_content",
            "grep" or "search" or "find" or "grep_search_files" => "grep_search",
            "run" or "exec" or "execute" or "command" or "bash" or "powershell" or "terminal" => "run_command",
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
        return !string.IsNullOrWhiteSpace(settings.DefaultCodingModel)
            ? settings.DefaultCodingModel.Trim()
            : "qwen2.5-coder";
    }

    private static string GetSystemPrompt(string? mode, bool autoApprove)
    {
        string currentMode = mode ?? "write";
        return $$"""
Sei Antigravity Code Agent, un assistente agentico di sviluppo software avanzato per Windows 10/11.
Modalità operativa: {{currentMode}}
Auto-Approvazione comandi: {{autoApprove}}

Hai accesso al workspace del progetto e puoi eseguire azioni sul filesystem e sulla shell locale tramite chiamate di strumento (tool call).

STRUMENTI DISPONIBILI:
1. list_dir({"relativePath": "string"})
2. read_file({"relativePath": "string", "startLine": 1, "endLine": 100})
3. write_file({"relativePath": "string", "content": "string"})
4. replace_file_content({"relativePath": "string", "targetContent": "string", "replacementContent": "string"})
5. grep_search({"query": "string", "searchPath": "string"})
6. run_command({"commandLine": "string", "isAsync": false})

REGOLE PER LE CHIAMATE DI STRUMENTO:
Per invocare uno strumento, rispondi RIGOROSAMENTE con un blocco di codice JSON racchiuso in ```json ... ``` con questo formato:
```json
{
  "tool": "nome_tool",
  "arguments": {
    "relativePath": "src/Main.cs"
  },
  "explanation": "Motivazione del passo..."
}
```

REGOLE COMPORTAMENTALI:
1. Se devi comprendere il progetto, usa list_dir o grep_search per individuare i file rilevanti.
2. Leggi sempre i file rilevanti con read_file prima di modificarli.
3. In modalità 'plan' NON chiamare mai write_file, replace_file_content o run_command. Limitati all'esplorazione e alla pianificazione.
4. Fai UNA SOLA chiamata a uno strumento per ogni passaggio.
5. Quando l'obiettivo è completato, fornisci la tua risposta finale spiegando il lavoro fatto in testo normale Markdown SENZA blocchi JSON di strumenti.
""";
    }
}

