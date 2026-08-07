using Microsoft.Extensions.AI;
using OnlyRag.Infrastructure.Ingestion;

namespace OnlyRag.Api.Services;

public sealed class MicrosoftExtensionsAiEmbeddingGeneratorAdapter : IStreamingEmbeddingGenerator
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> generator;

    public MicrosoftExtensionsAiEmbeddingGeneratorAdapter(IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public async Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
        string model,
        IReadOnlyList<string> contents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (contents.Count == 0)
        {
            return [];
        }

        var options = new EmbeddingGenerationOptions { ModelId = model };
        GeneratedEmbeddings<Embedding<float>> result = await generator.GenerateAsync(contents, options, cancellationToken).ConfigureAwait(false);
        return result.Select(e => (IReadOnlyList<float>)e.Vector.ToArray()).ToList();
    }
}
