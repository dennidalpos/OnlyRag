using System.Threading.Channels;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Infrastructure.Ingestion;

public interface IStreamingEmbeddingGenerator
{
    Task<IReadOnlyList<IReadOnlyList<float>>> GenerateEmbeddingsAsync(
        string model,
        IReadOnlyList<string> contents,
        CancellationToken cancellationToken);
}

public sealed class StreamingDocumentIngestionPipeline : IStreamingDocumentIngestionPipeline
{
    private readonly IDocumentRepository documents;
    private readonly DocumentTextChunker chunker;
    private readonly IEmbeddingRepository? embeddings;
    private readonly IQdrantVectorStore? vectorStore;
    private readonly IStreamingEmbeddingGenerator? embeddingGenerator;
    private readonly Retrieval.Graph.IEntityGraphExtractor? graphExtractor;
    private readonly Retrieval.Graph.IGraphRetrievalService? graphService;

    public StreamingDocumentIngestionPipeline(
        IDocumentRepository documents,
        DocumentTextChunker chunker,
        IEmbeddingRepository? embeddings = null,
        IQdrantVectorStore? vectorStore = null,
        IStreamingEmbeddingGenerator? embeddingGenerator = null,
        Retrieval.Graph.IEntityGraphExtractor? graphExtractor = null,
        Retrieval.Graph.IGraphRetrievalService? graphService = null)
    {
        this.documents = documents ?? throw new ArgumentNullException(nameof(documents));
        this.chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        this.embeddings = embeddings;
        this.vectorStore = vectorStore;
        this.embeddingGenerator = embeddingGenerator;
        this.graphExtractor = graphExtractor;
        this.graphService = graphService;
    }

    public async Task<DocumentIngestionResult> ProcessStreamAsync(
        ImportedDocument document,
        IAsyncEnumerable<ParsedPageBlock> pageBlockStream,
        DocumentIngestionOptions ingestionOptions,
        StreamingIngestionOptions streamingOptions,
        Func<DocumentIngestionProgress, CancellationToken, Task> saveProgressAsync,
        string? embeddingModel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageBlockStream);
        ArgumentNullException.ThrowIfNull(ingestionOptions);
        ArgumentNullException.ThrowIfNull(saveProgressAsync);

        var pageBlockChannelOptions = new BoundedChannelOptions(streamingOptions.PageBlockChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        var pageBlockChannel = Channel.CreateBounded<ParsedPageBlock>(pageBlockChannelOptions);

        var chunkBatchChannelOptions = new BoundedChannelOptions(streamingOptions.ChunkBatchChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        var chunkBatchChannel = Channel.CreateBounded<IngestedChunkBatch>(chunkBatchChannelOptions);

        var vectorBatchChannelOptions = new BoundedChannelOptions(streamingOptions.VectorBatchChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        var vectorBatchChannel = Channel.CreateBounded<VectorChunkBatch>(vectorBatchChannelOptions);

        int totalPages = 0;
        int totalChunks = 0;

        // Stage 1: Parser Stream Producer Task
        var parserTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var block in pageBlockStream.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await pageBlockChannel.Writer.WriteAsync(block, cancellationToken).ConfigureAwait(false);
                }
                pageBlockChannel.Writer.Complete();
            }
            catch (Exception ex)
            {
                pageBlockChannel.Writer.Complete(ex);
                throw;
            }
        }, cancellationToken);

        // Stage 2: Chunker Consumer & Producer Task
        var chunkerTask = Task.Run(async () =>
        {
            int currentChunkOrdinal = 0;
            long fakeChunkId = 1;
            try
            {
                await foreach (var block in pageBlockChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref totalPages);
                    string normalizedText = block.Text.Trim();

                    IReadOnlyList<IngestedDocumentChunk> chunks = string.IsNullOrWhiteSpace(normalizedText)
                        ? []
                        : chunker.CreateChunks(
                            normalizedText,
                            block.PageNumber,
                            block.PageNumber,
                            currentChunkOrdinal,
                            ingestionOptions);

                    await documents.SaveIngestedPageAsync(
                        document.Id,
                        new IngestedDocumentPage(block.PageNumber, normalizedText),
                        chunks,
                        block.PageNumber,
                        cancellationToken).ConfigureAwait(false);

                    if (chunks.Count > 0 && graphExtractor is not null && graphService is not null)
                    {
                        try
                        {
                            var allNodes = new List<EntityGraphNode>();
                            var allEdges = new List<EntityGraphEdge>();
                            foreach (var chunk in chunks)
                            {
                                var (nodes, edges) = graphExtractor.ExtractGraph(document.Id, fakeChunkId++, chunk.Text);
                                allNodes.AddRange(nodes);
                                allEdges.AddRange(edges);
                            }
                            if (allNodes.Count > 0 || allEdges.Count > 0)
                            {
                                await graphService.InsertGraphAsync(allNodes, allEdges, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            // Non-critical graph extraction fallback
                        }
                    }

                    currentChunkOrdinal += chunks.Count;
                    Interlocked.Add(ref totalChunks, chunks.Count);

                    var batch = new IngestedChunkBatch(document.Id, block.PageNumber, chunks);
                    await chunkBatchChannel.Writer.WriteAsync(batch, cancellationToken).ConfigureAwait(false);

                    await saveProgressAsync(
                        new DocumentIngestionProgress(
                            50,
                            $"Streaming page {block.PageNumber}",
                            new DocumentIngestionCheckpoint(1, document.Id, block.PageNumber + 1, totalPages, currentChunkOrdinal, "streaming")),
                        cancellationToken).ConfigureAwait(false);
                }
                chunkBatchChannel.Writer.Complete();
            }
            catch (Exception ex)
            {
                chunkBatchChannel.Writer.Complete(ex);
                throw;
            }
        }, cancellationToken);

        // Stage 3: Embedding Consumer & Producer Task
        bool canEmbed = !string.IsNullOrWhiteSpace(embeddingModel) && embeddingGenerator is not null && embeddings is not null;
        var embeddingTask = Task.Run(async () =>
        {
            try
            {
                if (!canEmbed)
                {
                    await foreach (var _ in chunkBatchChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        // Drain when embedding is disabled or unconfigured
                    }
                    vectorBatchChannel.Writer.Complete();
                    return;
                }

                int afterChunkIndex = 0;
                await foreach (var _ in chunkBatchChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (true)
                    {
                        var chunksNeedingEmbedding = await embeddings!.ListChunksNeedingEmbeddingAsync(
                            document.Id,
                            embeddingModel!,
                            afterChunkIndex,
                            streamingOptions.EmbeddingBatchSize,
                            cancellationToken).ConfigureAwait(false);

                        if (chunksNeedingEmbedding.Count == 0)
                        {
                            break;
                        }

                        var contents = chunksNeedingEmbedding.Select(c => c.Content).ToArray();
                        var vectors = await embeddingGenerator!.GenerateEmbeddingsAsync(
                            embeddingModel!,
                            contents,
                            cancellationToken).ConfigureAwait(false);

                        var dummyChunks = chunksNeedingEmbedding.Select(c => new IngestedDocumentChunk(
                            c.ChunkIndex,
                            c.ChunkIndex,
                            c.ChunkIndex,
                            c.Content,
                            c.Content.Length / 4,
                            c.ContentHash)).ToList();

                        var vectorBatch = new VectorChunkBatch(
                            document.Id,
                            embeddingModel!,
                            dummyChunks,
                            vectors);

                        await vectorBatchChannel.Writer.WriteAsync(vectorBatch, cancellationToken).ConfigureAwait(false);
                        afterChunkIndex = chunksNeedingEmbedding[^1].ChunkIndex + 1;
                    }
                }

                vectorBatchChannel.Writer.Complete();
            }
            catch (Exception ex)
            {
                vectorBatchChannel.Writer.Complete(ex);
                throw;
            }
        }, cancellationToken);

        // Stage 4: Vector Store Writer Task
        bool canWriteVector = embeddings is not null && vectorStore is not null && streamingOptions.EnableVectorStoreWriter;
        var vectorWriterTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var batch in vectorBatchChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!canWriteVector || batch.Embeddings.Count == 0)
                    {
                        continue;
                    }

                    var payloads = new List<QdrantChunkPayload>();
                    for (int i = 0; i < batch.Chunks.Count; i++)
                    {
                        var chunk = batch.Chunks[i];
                        var vector = batch.Embeddings[i];
                        if (vector.Count > 0)
                        {
                            payloads.Add(new QdrantChunkPayload(
                                chunk.Ordinal,
                                document.Id,
                                chunk.Ordinal,
                                batch.Model,
                                chunk.ContentHash,
                                vector));
                        }
                    }

                    if (payloads.Count > 0)
                    {
                        await vectorStore!.UpsertChunkBatchAsync(payloads, cancellationToken).ConfigureAwait(false);

                        for (int i = 0; i < payloads.Count; i++)
                        {
                            var p = payloads[i];
                            string collectionName = vectorStore.BuildCollectionName(batch.Model, p.Vector.Count);
                            string pointId = vectorStore.BuildPointId(p.ChunkId);

                            await embeddings!.MarkChunkIndexedAsync(
                                p.ChunkId,
                                batch.Model,
                                p.ContentHash,
                                p.Vector.Count,
                                collectionName,
                                pointId,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch
            {
                throw;
            }
        }, cancellationToken);

        await Task.WhenAll(parserTask, chunkerTask, embeddingTask, vectorWriterTask).ConfigureAwait(false);

        return new DocumentIngestionResult(totalPages, totalChunks);
    }
}
