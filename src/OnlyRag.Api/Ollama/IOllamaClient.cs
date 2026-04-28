using OnlyRag.Core;

namespace OnlyRag.Api.Ollama;

internal interface IOllamaClient
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task PullModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default);

    Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default);

    Task<string> GenerateChatAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        CancellationToken cancellationToken = default);

    Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
        string modelName,
        IReadOnlyList<string> inputs,
        int? numCtx = null,
        CancellationToken cancellationToken = default);
}
