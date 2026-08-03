using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;

namespace OnlyRag.Api.Ollama;

internal sealed class OllamaQueryEmbeddingGenerator : IQueryEmbeddingGenerator
{
    private readonly IOllamaSettingsService settingsService;
    private readonly IOllamaClient ollamaClient;
    private readonly OllamaQueryEmbeddingCache embeddingCache;

    public OllamaQueryEmbeddingGenerator(
        IOllamaSettingsService settingsService,
        IOllamaClient ollamaClient,
        OllamaQueryEmbeddingCache embeddingCache)
    {
        this.settingsService = settingsService;
        this.ollamaClient = ollamaClient;
        this.embeddingCache = embeddingCache;
    }

    public async Task<QueryEmbeddingResult> GenerateAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            OllamaSettings settings = await settingsService.GetAsync(cancellationToken);
            string model = OllamaSettingsService.NormalizeRequiredModelName(settings.DefaultEmbeddingModel);

            if (embeddingCache.TryGet(model, query, out IReadOnlyList<float> cachedVector))
            {
                return new QueryEmbeddingResult(model, cachedVector);
            }

            IReadOnlyList<IReadOnlyList<float>> embeddings = await ollamaClient.GenerateEmbeddingsAsync(
                model,
                [query],
                cancellationToken: cancellationToken);
            IReadOnlyList<float> vector = embeddings.Count == 0 ? [] : embeddings[0];
            
            if (vector.Count > 0)
            {
                embeddingCache.Set(model, query, vector);
            }

            return new QueryEmbeddingResult(model, vector);
        }
        catch (OllamaApiException ex)
        {
            throw new QueryEmbeddingUnavailableException(ex.Message, ex);
        }
    }
}
