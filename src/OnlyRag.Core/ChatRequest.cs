namespace OnlyRag.Core;

public sealed record ChatRequest(
    string Message,
    string Model,
    bool UseDocuments,
    IReadOnlyList<long>? SelectedDocumentIds,
    string? ConversationId);

