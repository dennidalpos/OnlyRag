namespace OnlyRag.Core;

public sealed record DocumentEmbeddingJobPayload(
    long DocumentId,
    string Model);
