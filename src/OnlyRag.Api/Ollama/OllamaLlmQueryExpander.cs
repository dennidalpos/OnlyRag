using OnlyRag.Core;
using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api.Ollama;

internal sealed class OllamaLlmQueryExpander : ILlmQueryExpander
{
    private readonly IOllamaSettingsService settingsService;
    private readonly IOllamaClient ollamaClient;
    private readonly ILoggingService? logger;

    public OllamaLlmQueryExpander(
        IOllamaSettingsService settingsService,
        IOllamaClient ollamaClient,
        ILoggingService? logger = null)
    {
        this.settingsService = settingsService;
        this.ollamaClient = ollamaClient;
        this.logger = logger;
    }

    public async Task<string?> GenerateExpansionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

            OllamaSettings settings = await settingsService.GetAsync(timeoutCts.Token);
            string model = OllamaSettingsService.NormalizeRequiredModelName(settings.DefaultChatModel);

            var messages = new List<OllamaChatMessage>
            {
                new("system", "You are a concise search query expansion assistant. Provide direct query reformulations or passages without conversational filler or introductory text."),
                new("user", prompt)
            };

            string response = await ollamaClient.GenerateChatAsync(
                model,
                messages,
                numCtx: 4096,
                cancellationToken: timeoutCts.Token);

            return response;
        }
        catch (Exception ex)
        {
            logger?.LogWarning("OllamaLlmQueryExpander", $"LLM query expansion skipped or timed out: {ex.Message}");
            return null;
        }
    }
}
