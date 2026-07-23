using OnlyRag.Core;

namespace OnlyRag.Api.Ollama;

internal interface IOllamaClient
{
    Task TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OllamaModelSummary>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    Task<IReadOnlyList<OllamaRunningModelResponse>> ListRunningModelsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<OllamaRunningModelResponse>>([]);
    }

    Task PullModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task PullModelAsync(
        string modelName,
        Func<OllamaModelPullProgress, CancellationToken, Task> onProgress,
        CancellationToken cancellationToken = default);

    Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task ChatSmokeAsync(string modelName, CancellationToken cancellationToken = default);

    Task EmbeddingsSmokeAsync(string modelName, CancellationToken cancellationToken = default);

    Task<string> GenerateChatAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        int? numCtx = null,
        CancellationToken cancellationToken = default);

    async IAsyncEnumerable<string> GenerateChatStreamAsync(
        string modelName,
        IReadOnlyList<OllamaChatMessage> messages,
        int? numCtx = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string response = await GenerateChatAsync(modelName, messages, numCtx, cancellationToken);
        yield return response;
    }

    Task<OllamaModelDetails> ShowModelAsync(string modelName, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
        string modelName,
        IReadOnlyList<string> inputs,
        int? numCtx = null,
        CancellationToken cancellationToken = default);
}
