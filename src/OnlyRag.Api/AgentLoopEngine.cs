using System.Collections.Concurrent;
using System.Text.Json;
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

            string responseText = await ollamaClient.GenerateChatAsync(
                model,
                messages,
                numCtx: numCtx,
                cancellationToken: cancellationToken);

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

    private static AgentToolCall? TryExtractToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        int jsonStart = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (jsonStart != -1)
        {
            int blockStart = jsonStart + 7;
            int jsonEnd = text.IndexOf("```", blockStart);
            if (jsonEnd != -1)
            {
                string jsonBody = text.Substring(blockStart, jsonEnd - blockStart).Trim();
                return ParseToolCallJson(jsonBody);
            }
        }

        // Tenta il parsing diretto se l'intero testo è un oggetto JSON
        string trimmed = text.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return ParseToolCallJson(trimmed);
        }

        return null;
    }

    private static AgentToolCall? ParseToolCallJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("tool", out var toolProp))
            {
                string toolName = toolProp.GetString() ?? "";
                string argsJson = root.TryGetProperty("arguments", out var argsProp) ? argsProp.GetRawText() : "{}";
                string? explanation = root.TryGetProperty("explanation", out var expProp) ? expProp.GetString() : null;

                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    return new AgentToolCall(
                        CallId: $"call_{Guid.NewGuid():N}"[..10],
                        ToolName: toolName,
                        ArgumentsJson: argsJson,
                        Explanation: explanation);
                }
            }
        }
        catch
        {
            // Ignora JSON non valido
        }

        return null;
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
Per invocare uno strumento, rispondi RIGOROSAMENTE ed ESCLUSIVAMENTE con un blocco di codice JSON racchiuso in ```json ... ``` con questo formato:
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
4. Quando l'obiettivo è completato, fornisci una risposta finale in testo normale Markdown SENZA blocchi json tool.
""";
    }
}
