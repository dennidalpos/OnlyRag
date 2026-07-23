using System.Text.RegularExpressions;
using OnlyRag.Api.Ollama;
using OnlyRag.Core;

namespace OnlyRag.Api;

internal sealed class CodingService
{
    private const int DefaultCodingNumCtx = 16384;

    private readonly IOllamaClient ollamaClient;
    private readonly IOllamaSettingsService settingsService;

    public CodingService(
        IOllamaClient ollamaClient,
        IOllamaSettingsService settingsService)
    {
        this.ollamaClient = ollamaClient;
        this.settingsService = settingsService;
    }

    public async Task<CodingTaskResponse> GenerateCodeAsync(
        CodingTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        string model = await ResolveModelAsync(request.Model, cancellationToken);
        string language = string.IsNullOrWhiteSpace(request.Language) ? "csharp" : request.Language.Trim().ToLowerInvariant();

        List<OllamaChatMessage> messages = BuildPromptMessages(request, language);

        string rawResponse = await ollamaClient.GenerateChatAsync(
            model,
            messages,
            numCtx: DefaultCodingNumCtx,
            cancellationToken: cancellationToken);

        (string generatedCode, string explanation) = ExtractCodeAndExplanation(rawResponse, language);

        var suggestions = new List<string>
        {
            $"Verificare la sintassi e compilare per {language}.",
            "Eseguire i test di unità corrispondenti prima del deploy."
        };

        return new CodingTaskResponse(
            GeneratedCode: generatedCode,
            Explanation: explanation,
            Language: language,
            TargetFilePath: request.TargetFilePath,
            ExecutionSuggestions: suggestions);
    }

    public async Task<CodeRefactorResponse> RefactorCodeAsync(
        CodeRefactorRequest request,
        CancellationToken cancellationToken = default)
    {
        string model = await ResolveModelAsync(request.Model, cancellationToken);
        string language = string.IsNullOrWhiteSpace(request.Language) ? "csharp" : request.Language.Trim().ToLowerInvariant();

        string systemPrompt = "Sei un esperto di Refactoring e Clean Code. " +
            "Il tuo obiettivo è ristrutturare il codice fornito per soddisfare l'obiettivo richiesto (" + request.Goal + "), mantenendo intatta la logica di business. " +
            "Restituisci il codice rifattorizzato in un blocco di codice markdown seguito da una spiegazione sintetica dei cambiamenti.";

        string userMessage = $"Linguaggio: {language}\nObiettivo Refactoring: {request.Goal}\n";
        if (!string.IsNullOrWhiteSpace(request.Instructions))
        {
            userMessage += $"Istruzioni aggiuntive: {request.Instructions}\n";
        }
        userMessage += $"Codice Originale:\n```\n{request.OriginalCode}\n```";

        var messages = new List<OllamaChatMessage>
        {
            new("system", systemPrompt),
            new("user", userMessage)
        };

        string rawResponse = await ollamaClient.GenerateChatAsync(
            model,
            messages,
            numCtx: DefaultCodingNumCtx,
            cancellationToken: cancellationToken);

        (string modifiedCode, string explanation) = ExtractCodeAndExplanation(rawResponse, language);

        return new CodeRefactorResponse(
            OriginalCode: request.OriginalCode,
            ModifiedCode: modifiedCode,
            Explanation: explanation,
            Language: language);
    }

    public async Task<CodeDiagnoseResponse> DiagnoseCodeAsync(
        CodeDiagnoseRequest request,
        CancellationToken cancellationToken = default)
    {
        string model = await ResolveModelAsync(request.Model, cancellationToken);
        string language = string.IsNullOrWhiteSpace(request.Language) ? "csharp" : request.Language.Trim().ToLowerInvariant();

        string systemPrompt = "Sei un esperto sviluppatore e debugger. " +
            "Analizza l'errore / stack trace fornito e il relativo contesto di codice. " +
            "Struttura la risposta nel seguente formato:\n" +
            "### Analisi Causa Radice\n[Descrizione chiara del problema]\n\n" +
            "### Codice Corretto\n```\n[Codice corretto]\n```";

        string userMessage = $"Linguaggio: {language}\nLog Errore / Stack Trace:\n```\n{request.ErrorLog}\n```\n";
        if (!string.IsNullOrWhiteSpace(request.CodeContext))
        {
            userMessage += $"Contesto Codice:\n```\n{request.CodeContext}\n```";
        }

        var messages = new List<OllamaChatMessage>
        {
            new("system", systemPrompt),
            new("user", userMessage)
        };

        string rawResponse = await ollamaClient.GenerateChatAsync(
            model,
            messages,
            numCtx: DefaultCodingNumCtx,
            cancellationToken: cancellationToken);

        (string fixedCode, string analysis) = ExtractCodeAndExplanation(rawResponse, language);

        string diff = GenerateSimpleDiff(request.CodeContext ?? "", fixedCode);

        return new CodeDiagnoseResponse(
            RootCauseAnalysis: string.IsNullOrWhiteSpace(analysis) ? rawResponse : analysis,
            SuggestedFixCode: fixedCode,
            FixedCodeDiff: diff,
            Language: language);
    }

    public async IAsyncEnumerable<string> GenerateCodeStreamAsync(
        CodingTaskRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string model = await ResolveModelAsync(request.Model, cancellationToken);
        string language = string.IsNullOrWhiteSpace(request.Language) ? "csharp" : request.Language.Trim().ToLowerInvariant();

        List<OllamaChatMessage> messages = BuildPromptMessages(request, language);

        OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
        int numCtx = settings.CodingNumCtx ?? DefaultCodingNumCtx;

        await foreach (string chunk in ollamaClient.GenerateChatStreamAsync(model, messages, numCtx: numCtx, cancellationToken: cancellationToken))
        {
            yield return chunk;
        }
    }

    private static List<OllamaChatMessage> BuildPromptMessages(CodingTaskRequest request, string language)
    {
        string personaPrompt = GetPersonaSystemPrompt(request.Persona);
        bool isPlanMode = string.Equals(request.Mode, "plan", StringComparison.OrdinalIgnoreCase);

        string systemInstruction;
        if (isPlanMode)
        {
            systemInstruction = "Sei un Senior Software Architect e Code Planner in modalità PIANO / LETTURA (Antigravity Plan Mode). " +
                "Il tuo compito è analizzare la richiesta dell'utente, la struttura del progetto e i file forniti. " +
                "Fornisci una risposta ben strutturata in markdown con: 1) Analisi dei Requisiti & Architettura, 2) Piano di Implementazione passo-passo, 3) File impattati e dipendenze, 4) Raccomandazioni e pseudocodice di riferimento.";
        }
        else
        {
            systemInstruction = $"{personaPrompt}\nLinguaggio target: {language}. " +
                "Rispondi fornendo il codice completo e funzionante racchiuso in blocchi di codice markdown con la relativa sintassi. " +
                "Indica 'Target File: [percorso]' se stai proponendo una modifica a un file specifico, seguito da una breve spiegazione architetturale.";
        }

        var messages = new List<OllamaChatMessage>
        {
            new("system", systemInstruction)
        };

        if (!string.IsNullOrWhiteSpace(request.WorkspaceSummary))
        {
            messages.Add(new("user", $"[INDICE E STRUTTURA DEL PROGETTO WORKSPACE (Antigravity Index)]\n{request.WorkspaceSummary}"));
        }

        if (!string.IsNullOrWhiteSpace(request.CodeContext))
        {
            messages.Add(new("user", $"Contesto del codice / file allegati:\n```\n{request.CodeContext}\n```"));
        }

        string userPrompt = request.Prompt;
        if (!string.IsNullOrWhiteSpace(request.TargetFilePath))
        {
            userPrompt = $"Target File: {request.TargetFilePath}\n{userPrompt}";
        }

        messages.Add(new("user", userPrompt));
        return messages;
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

    private static string GetPersonaSystemPrompt(string? persona)
    {
        return (persona?.Trim().ToLowerInvariant()) switch
        {
            "free_prompt" => "Sei un assistente di programmazione altamente flessibile. Rispondi direttamente alle istruzioni dell'utente senza applicare vincoli di formato rigidi.",
            "speedrunner" => "Sei un Vibe Coder ad altissima velocità. Scrivi codice pronto all'uso, autonomo e perfettamente funzionante, focalizzandoti sull'ottenere il risultato nel minor tempo possibile.",
            "clean_code" => "Sei uno specialista di Clean Code. Scrivi codice altamente leggibile, modulare, auto-documentato, privo di duplicazioni e con gestione avanzata degli errori.",
            "security_auditor" => "Sei un esperto di Cybersecurity e Code Auditor. Risolvi bug, previeni memory leak, sanitizza gli input ed evita ogni possibile vulnerabilità di sicurezza.",
            _ => "Sei un Senior Software Architect e Vibe Coder esperto. Il tuo compito è progettare e scrivere codice pulito, scalabile, secondo i principi SOLID e le migliori pratiche architetturali."
        };
    }

    private static (string Code, string Explanation) ExtractCodeAndExplanation(string responseText, string language)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return (string.Empty, string.Empty);
        }

        Match match = Regex.Match(responseText, @"```(?:\w+)?\r?\n(.*?)\r?\n```", RegexOptions.Singleline);
        if (match.Success)
        {
            string code = match.Groups[1].Value.Trim();
            string explanation = responseText.Replace(match.Value, "").Trim();
            return (code, explanation);
        }

        return (responseText.Trim(), "Codice generato senza spiegazione separata.");
    }

    private static string GenerateSimpleDiff(string original, string modified)
    {
        if (string.IsNullOrWhiteSpace(original))
        {
            return $"+ {modified}";
        }

        return $"- {original}\n+ {modified}";
    }
}
