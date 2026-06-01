using System.Text.Json.Serialization;

namespace OnlyRag.Api.Ollama;

internal sealed partial class OllamaClient
{
    private sealed record OllamaRequestContext(Uri BaseUri, TimeSpan Timeout);

    private sealed record OllamaTagsResponse(IReadOnlyList<OllamaModelPayload> Models);

    private sealed record OllamaModelPayload(
        string Name,
        string Model,
        [property: JsonPropertyName("modified_at")]
        DateTimeOffset? ModifiedAt,
        long Size,
        string? Digest,
        OllamaModelDetailsPayload? Details);

    private sealed record OllamaModelDetailsPayload(
        string? Family,
        [property: JsonPropertyName("parameter_size")] string? ParameterSize,
        [property: JsonPropertyName("quantization_level")] string? QuantizationLevel);

    private sealed record OllamaVersionResponse(string? Version);

    private sealed record OllamaPsResponse(IReadOnlyList<OllamaRunningModelPayload> Models);

    private sealed record OllamaRunningModelPayload(
        string Name,
        string Model,
        long? Size,
        [property: JsonPropertyName("size_vram")]
        long? SizeVram,
        string? Digest,
        [property: JsonPropertyName("context_length")]
        int? ContextLength,
        [property: JsonPropertyName("model_info")]
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? ModelInfo);

    private sealed record PullResponse(
        string Status,
        long? Total,
        long? Completed,
        string? Digest,
        string? Layer);

    private sealed record OllamaChatResponse(bool Done, OllamaChatResponseMessage? Message);

    private sealed record OllamaChatResponseMessage(string Role, string Content);

    private sealed record EmbeddingResponse(IReadOnlyList<IReadOnlyList<float>> Embeddings);

    private sealed record OllamaErrorResponse(string? Error);

    private sealed record OllamaShowResponse(
        [property: JsonPropertyName("model_info")]
        IReadOnlyDictionary<string, System.Text.Json.JsonElement>? ModelInfo);
}
