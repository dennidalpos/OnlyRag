using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Ingestion;

public sealed record ParsedPageBlock(
    long DocumentId,
    int PageNumber,
    string Text,
    string SourceKind = "text",
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record IngestedChunkBatch(
    long DocumentId,
    int PageNumber,
    IReadOnlyList<IngestedDocumentChunk> Chunks);

public sealed record VectorChunkBatch(
    long DocumentId,
    string Model,
    IReadOnlyList<IngestedDocumentChunk> Chunks,
    IReadOnlyList<IReadOnlyList<float>> Embeddings);

public sealed record StreamingIngestionOptions(
    int PageBlockChannelCapacity = 100,
    int ChunkBatchChannelCapacity = 50,
    int VectorBatchChannelCapacity = 50,
    int EmbeddingBatchSize = 16,
    bool EnableVectorStoreWriter = true);
