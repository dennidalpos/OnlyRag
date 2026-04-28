using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api.Ollama;

internal sealed class OllamaQueryEmbeddingGenerator : IQueryEmbeddingGenerator
{
    private readonly IOllamaSettingsService settingsService;
    private readonly IOllamaClient ollamaClient;

    public OllamaQueryEmbeddingGenerator(
        IOllamaSettingsService settingsService,
        IOllamaClient ollamaClient)
    {
        this.settingsService = settingsService;
        this.ollamaClient = ollamaClient;
    }

    public async Task<QueryEmbeddingResult> GenerateAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
            string model = OllamaSettingsService.NormalizeRequiredModelName(settings.DefaultEmbeddingModel);
            IReadOnlyList<IReadOnlyList<float>> embeddings = await ollamaClient.GenerateEmbeddingsAsync(
                model,
                [query],
                cancellationToken: cancellationToken);
            IReadOnlyList<float> vector = embeddings.Count == 0 ? [] : embeddings[0];
            return new QueryEmbeddingResult(model, vector);
        }
        catch (OllamaApiException ex)
        {
            throw new QueryEmbeddingUnavailableException(ex.Message, ex);
        }
    }
}
