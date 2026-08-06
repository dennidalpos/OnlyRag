using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Agent;

public sealed class AgentQueryIntentRouter : IAgentQueryIntentRouter
{
    private static readonly Regex ActionKeywordsRegex = new(
        @"\b(crea|modifica|aggiungi|rimuovi|refactor|fix|bug|build|test|implementa|scrivi|elimina|cambia|progetta|aggiorna)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RetrievalKeywordsRegex = new(
        @"\b(cerca|trova|ricerca|documenti|file|dove|spiega|cos'è|cosa fa|sommario|estrai|confronta|differenza)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DecompositionKeywordsRegex = new(
        @"\b(architettura|sistema|progetto|multi-step|modulo|migrazione|riscrittura|integrazione)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<AgentQueryIntentResult> RouteIntentAsync(
        string userPrompt,
        string? workspaceRoot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userPrompt);
        string text = userPrompt.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new AgentQueryIntentResult(
                AgentIntentKind.DirectAnswer,
                "Prompt vuoto",
                RequiresPlan: false,
                MinimumPlan: null,
                Confidence: 1.0f));
        }

        bool isAction = ActionKeywordsRegex.IsMatch(text);
        bool isRetrieval = RetrievalKeywordsRegex.IsMatch(text);
        bool isDecomposition = DecompositionKeywordsRegex.IsMatch(text) || text.Length > 200;

        AgentIntentKind intent;
        bool requiresPlan;
        float confidence;
        string rationale;

        if (isDecomposition || (isAction && text.Length > 100))
        {
            intent = AgentIntentKind.Decomposition;
            requiresPlan = true;
            confidence = 0.9f;
            rationale = "Task complesso di codice/architettura che richiede decomposizione ed esecuzione pianificata.";
        }
        else if (isAction)
        {
            intent = AgentIntentKind.Action;
            requiresPlan = true;
            confidence = 0.85f;
            rationale = "Richiesta di modifica diretta o esecuzione codice.";
        }
        else if (isRetrieval)
        {
            intent = text.Contains("confronta", StringComparison.OrdinalIgnoreCase) || text.Contains("differenza", StringComparison.OrdinalIgnoreCase)
                ? AgentIntentKind.Comparison
                : (text.Contains("estrai", StringComparison.OrdinalIgnoreCase) ? AgentIntentKind.Extraction : AgentIntentKind.Retrieval);
            requiresPlan = false;
            confidence = 0.88f;
            rationale = "Richiesta informativa o di recupero contesto.";
        }
        else
        {
            intent = AgentIntentKind.DirectAnswer;
            requiresPlan = false;
            confidence = 0.95f;
            rationale = "Domanda semplice o conversazionale.";
        }

        AgentTypedPlan? minimumPlan = null;
        if (requiresPlan)
        {
            string planId = $"plan_{Guid.NewGuid():N}";
            List<AgentTaskStep> steps = new()
            {
                new AgentTaskStep(
                    StepId: "step_1_inspect",
                    Description: "Ispezione e analisi iniziale dei requisiti e del contesto",
                    Preconditions: [new AgentPrecondition("Workspace", workspaceRoot ?? ".", "Workspace pronto")],
                    Postconditions: [new AgentPostcondition("Analysis", "Verified", "Analisi completata")],
                    ExpectedToolName: "view_file",
                    ExpectedCommand: null,
                    Status: AgentStepStatus.Pending),
                new AgentTaskStep(
                    StepId: "step_2_execute",
                    Description: "Applicazione delle modifiche richieste",
                    Preconditions: [new AgentPrecondition("Step", "step_1_inspect", "Ispezione completata")],
                    Postconditions: [new AgentPostcondition("Code", "Modified", "Codice modificato")],
                    ExpectedToolName: "replace_file_content",
                    ExpectedCommand: null,
                    Status: AgentStepStatus.Pending),
                new AgentTaskStep(
                    StepId: "step_3_verify",
                    Description: "Verifica deterministica tramite build o test",
                    Preconditions: [new AgentPrecondition("Step", "step_2_execute", "Modifiche applicate")],
                    Postconditions: [new AgentPostcondition("BuildTest", "Passed", "Verifica superata")],
                    ExpectedToolName: "run_command",
                    ExpectedCommand: "pwsh .\\scripts\\Test-Code.ps1",
                    Status: AgentStepStatus.Pending)
            };

            List<AgentCompletionCriterion> criteria = new()
            {
                new AgentCompletionCriterion("crit_test", "Esecuzione test o gate di verifica", AgentCompletionVerificationKind.Command, ExpectedToolName: "run_command", ExpectedCommand: "pwsh .\\scripts\\Test-Code.ps1", Required: true)
            };

            minimumPlan = new AgentTypedPlan(planId, text, intent, steps, criteria, IsBinding: true);
        }

        return Task.FromResult(new AgentQueryIntentResult(intent, rationale, requiresPlan, minimumPlan, confidence));
    }
}
