namespace OnlyRag.Core;

public sealed record ChatRequest(
    string Message,
    string Model,
    bool UseDocuments,
    IReadOnlyList<long>? SelectedDocumentIds,
    string? ConversationId);

public sealed record ChatResponse(
    string ConversationId,
    string Model,
    string Answer,
    bool UsedDocuments,
    IReadOnlyList<ChatSource> Sources,
    IReadOnlyList<ChatNotice> Notices);

public sealed record ChatStreamChunkEvent(
    string EventType,
    string? ConversationId = null,
    string? Model = null,
    string? Content = null,
    IReadOnlyList<ChatSource>? Sources = null,
    IReadOnlyList<ChatNotice>? Notices = null);

public sealed record ChatSource(
    long DocumentId,
    string DocumentName,
    int? PageStart,
    int? PageEnd,
    long ChunkId,
    string Snippet,
    double Score);

public sealed record ChatNotice(
    string Code,
    string Message);

public sealed record OllamaChatMessage(
    string Role,
    string Content);

public sealed record OllamaModelSummary(
    string Name,
    string Model,
    DateTimeOffset? ModifiedAt,
    long Size,
    string? Digest,
    string? Family,
    string? ParameterSize,
    string? QuantizationLevel);

public sealed record OllamaModelDetails(string Name, int? NumCtx);

public sealed record OllamaModelsResponse(IReadOnlyList<OllamaModelSummary> Models);

public sealed record OllamaStatusResponse(
    string State,
    bool IsReachable,
    string BaseUrl,
    int InstalledModelCount,
    string Message,
    string? Suggestion,
    string? Version = null,
    IReadOnlyList<OllamaRunningModelResponse>? RunningModels = null);

public sealed record OllamaRunningModelResponse(
    string Name,
    string Model,
    long? Size,
    long? SizeVram,
    string? Digest,
    int? ContextLength);

public sealed record OllamaEndpointOptions
{
    public const string DefaultBaseUrl = "http://localhost:11434";

    public Uri BaseUri { get; init; } = new(DefaultBaseUrl);
}

public sealed record PullOllamaModelRequest(string Name);

public sealed record OllamaModelPullStartResponse(
    string JobId,
    string ModelName,
    string Status,
    string Message);

public sealed record OllamaModelPullProgress(
    string Status,
    long? Total,
    long? Completed,
    int? ProgressPercent,
    string? Digest = null,
    string? Layer = null);

public sealed record OllamaModelPullJobPayload(string ModelName);
