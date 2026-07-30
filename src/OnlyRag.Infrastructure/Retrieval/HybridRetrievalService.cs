using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class HybridRetrievalService : IHybridRetrievalService
{
    private readonly IDocumentRepository documents;
    private readonly IEmbeddingRepository embeddings;
    private readonly IKeywordSearchService keywordSearch;
    private readonly IQdrantVectorStore vectorSearch;
    private readonly IRetrievalChunkRepository chunks;
    private readonly IQueryEmbeddingGenerator queryEmbeddingGenerator;
    private readonly IReRankerService reRanker;
    private readonly IQueryTransformationService queryTransformer;
    private readonly ParentChildChunkResolver parentChildResolver;
    private readonly CragEvaluator cragEvaluator;
    private readonly HybridRetrievalOptions options;
    private readonly ISettingsRepository? settings;

    public HybridRetrievalService(
        IDocumentRepository documents,
        IEmbeddingRepository embeddings,
        IKeywordSearchService keywordSearch,
        IQdrantVectorStore vectorSearch,
        IRetrievalChunkRepository chunks,
        IQueryEmbeddingGenerator queryEmbeddingGenerator,
        IReRankerService? reRanker = null,
        IQueryTransformationService? queryTransformer = null,
        ParentChildChunkResolver? parentChildResolver = null,
        CragEvaluator? cragEvaluator = null,
        HybridRetrievalOptions? options = null,
        ISettingsRepository? settings = null)
    {
        this.documents = documents;
        this.embeddings = embeddings;
        this.keywordSearch = keywordSearch;
        this.vectorSearch = vectorSearch;
        this.chunks = chunks;
        this.queryEmbeddingGenerator = queryEmbeddingGenerator;
        this.reRanker = reRanker ?? new HeuristicReRankerService();
        this.queryTransformer = queryTransformer ?? new OllamaQueryTransformationService();
        this.parentChildResolver = parentChildResolver ?? new ParentChildChunkResolver();
        this.cragEvaluator = cragEvaluator ?? new CragEvaluator();
        this.options = options ?? HybridRetrievalOptions.Default;
        this.settings = settings;
    }

    public async Task<DocumentSearchResponse> SearchAsync(
        DocumentSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        string query = request.Query?.Trim() ?? string.Empty;
        IReadOnlyList<long> requestedDocumentIds = request.DocumentIds ?? [];
        long[] documentIds = requestedDocumentIds
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        int finalLimit = Math.Min(
            NormalizeTopK(request.TopK),
            await GetMaxContextChunksAsync(cancellationToken));

        if (string.IsNullOrWhiteSpace(query) || documentIds.Length == 0)
        {
            return new DocumentSearchResponse(
                [],
                await BuildDocumentStatusesAsync(documentIds, model: null, cancellationToken),
                "none",
                vectorSearch.BackendName,
                options.MaxContextCharacters);
        }

        List<RetrievalNotice> notices = [];

        // 1. Query Transformation
        QueryTransformationResult transformation = await queryTransformer.TransformAsync(
            query,
            QueryTransformationStrategy.MultiQuery,
            cancellationToken);

        // 2. Coarse Hybrid Retrieval (1st stage: FTS5 + Qdrant HNSW in parallel via RRF)
        Task<KeywordSearchResponse> keywordTask = keywordSearch.SearchAsync(
            query,
            documentIds,
            options.KeywordTopK,
            cancellationToken);

        Task<QueryEmbeddingResult?> embeddingTask = TryGenerateQueryEmbeddingAsync(query, notices, cancellationToken);

        await Task.WhenAll(keywordTask, embeddingTask);

        KeywordSearchResponse keywordResponse = await keywordTask;
        QueryEmbeddingResult? queryEmbedding = await embeddingTask;

        VectorSearchAttempt vectorSearchAttempt = queryEmbedding is null
            ? new VectorSearchAttempt([], "Qdrant unavailable")
            : await TryVectorSearchAsync(queryEmbedding, documentIds, notices, cancellationToken);

        // 3. Merging via Reciprocal Rank Fusion (RRF)
        IReadOnlyList<DocumentSearchResult> coarseResults = await RrfMergeResultsAsync(
            query,
            keywordResponse.Results,
            vectorSearchAttempt.Results,
            options.VectorTopK,
            cancellationToken);

        // 4. Bulk fetch chunks & Parent-Child resolution BEFORE 2nd-Stage Re-ranking
        long[] coarseChunkIds = coarseResults.Select(r => r.ChunkId).ToArray();
        IReadOnlyDictionary<long, SearchChunk> rawChunkMap = await chunks.GetChunksAsync(coarseChunkIds, cancellationToken);

        Dictionary<long, SearchChunk> resolvedChunkMap = new();
        List<ReRankCandidate> candidates = new();

        foreach (DocumentSearchResult coarse in coarseResults)
        {
            SearchChunk? rawChunk = rawChunkMap.TryGetValue(coarse.ChunkId, out SearchChunk? c) ? c : null;
            SearchChunk? resolved = rawChunk is not null ? parentChildResolver.Resolve(rawChunk) : null;
            if (resolved is not null)
            {
                resolvedChunkMap[coarse.ChunkId] = resolved;
            }
            string textToRank = resolved?.ParentContent ?? coarse.Snippet;
            candidates.Add(new ReRankCandidate(coarse.ChunkId, textToRank));
        }

        IReadOnlyList<ReRankResult> reRankedScores = await reRanker.ReRankAsync(query, candidates, cancellationToken);
        Dictionary<long, double> scoreMap = reRankedScores.ToDictionary(s => s.ChunkId, s => s.Score);

        List<DocumentSearchResult> finalResults = [];
        foreach (DocumentSearchResult coarse in coarseResults)
        {
            double reRankScore = scoreMap.TryGetValue(coarse.ChunkId, out double score) ? score : coarse.Score;
            SearchChunk? resolved = resolvedChunkMap.TryGetValue(coarse.ChunkId, out SearchChunk? r) ? r : null;

            finalResults.Add(coarse with
            {
                Score = reRankScore,
                ReRankScore = reRankScore,
                ParentContent = resolved?.ParentContent ?? coarse.Snippet,
                SectionHeading = resolved?.SectionHeading,
                ChunkLevel = resolved?.ChunkLevel ?? "Child"
            });
        }

        IReadOnlyList<DocumentSearchResult> ranked = finalResults
            .OrderByDescending(r => r.ReRankScore ?? r.Score)
            .Take(finalLimit)
            .ToList();

        // 5. Self-Corrective RAG (CRAG) Confidence Check
        // Note: RRF scores are raw values (typically 0.01-0.05 range). Threshold 0.08 catches
        // genuinely low-confidence retrievals without false positives on normal results.
        CragEvaluationResult cragResult = cragEvaluator.Evaluate(ranked, 0.08d);
        if (!cragResult.IsConfident)
        {
            notices.Add(new RetrievalNotice("crag_low_confidence", cragResult.SummaryNotice));
        }

        IReadOnlyList<DocumentSearchDocumentStatus> documentStatuses = await BuildDocumentStatusesAsync(
            documentIds,
            queryEmbedding?.Model,
            cancellationToken);

        return new DocumentSearchResponse(
            ranked,
            documentStatuses,
            keywordResponse.BackendName,
            vectorSearchAttempt.BackendName,
            options.MaxContextCharacters)
        {
            Notices = notices
        };
    }

    private int NormalizeTopK(int? requestedTopK)
    {
        int value = requestedTopK ?? options.DefaultTopK;
        return Math.Clamp(value, 1, Math.Max(1, options.MaxTopK));
    }

    private async Task<int> GetMaxContextChunksAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return options.DefaultTopK;
        }

        string? value = await settings.GetValueAsync("performance.maxContextChunks", cancellationToken);
        return int.TryParse(value, out int parsed)
            ? Math.Clamp(parsed, 1, options.MaxTopK)
            : options.DefaultTopK;
    }

    private async Task<QueryEmbeddingResult?> TryGenerateQueryEmbeddingAsync(
        string query,
        List<RetrievalNotice> notices,
        CancellationToken cancellationToken)
    {
        try
        {
            QueryEmbeddingResult result = await queryEmbeddingGenerator.GenerateAsync(query, cancellationToken);
            if (result.Vector.Count == 0)
            {
                throw new InvalidOperationException("Embedding query vuoto: retrieval Qdrant non eseguibile.");
            }

            return result;
        }
        catch (Exception ex) when (ex is QueryEmbeddingUnavailableException or InvalidOperationException or NotSupportedException)
        {
            notices.Add(new RetrievalNotice(
                "vector_embedding_unavailable",
                $"Embedding query non disponibile: {ex.Message} Continuo con retrieval keyword locale."));
            return null;
        }
    }

    private async Task<VectorSearchAttempt> TryVectorSearchAsync(
        QueryEmbeddingResult queryEmbedding,
        IReadOnlyCollection<long> documentIds,
        List<RetrievalNotice> notices,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<VectorSearchResult> results = await vectorSearch.SearchAsync(
                queryEmbedding.Model,
                queryEmbedding.Vector,
                documentIds,
                options.VectorTopK,
                cancellationToken);
            return new VectorSearchAttempt(results, vectorSearch.BackendName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TimeoutException)
        {
            notices.Add(new RetrievalNotice(
                "vector_search_unavailable",
                $"Vector search non disponibile: {ex.Message} Continuo con retrieval keyword locale."));
            return new VectorSearchAttempt([], $"{vectorSearch.BackendName} unavailable");
        }
    }

    private async Task<IReadOnlyList<DocumentSearchResult>> RrfMergeResultsAsync(
        string query,
        IReadOnlyList<KeywordSearchResult> keywordResults,
        IReadOnlyList<VectorSearchResult> vectorResults,
        int limit,
        CancellationToken cancellationToken)
    {
        const double k = 60d; // RRF constant
        Dictionary<long, double> rrfScores = [];

        for (int rank = 0; rank < keywordResults.Count; rank++)
        {
            long chunkId = keywordResults[rank].ChunkId;
            rrfScores[chunkId] = rrfScores.GetValueOrDefault(chunkId, 0d) + (1d / (k + rank + 1));
        }

        for (int rank = 0; rank < vectorResults.Count; rank++)
        {
            long chunkId = vectorResults[rank].ChunkId;
            rrfScores[chunkId] = rrfScores.GetValueOrDefault(chunkId, 0d) + (1d / (k + rank + 1));
        }

        long[] chunkIds = rrfScores.Keys.ToArray();
        IReadOnlyDictionary<long, SearchChunk> chunkMap = await chunks.GetChunksAsync(chunkIds, cancellationToken);

        List<DocumentSearchResult> results = [];
        foreach (KeyValuePair<long, double> kvp in rrfScores.OrderByDescending(kv => kv.Value).Take(limit))
        {
            if (!chunkMap.TryGetValue(kvp.Key, out SearchChunk? chunk))
            {
                continue;
            }

            results.Add(new DocumentSearchResult(
                chunk.DocumentId,
                chunk.DocumentName,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.ChunkId,
                chunk.Content,
                Math.Round(kvp.Value * 30d, 4))); // Normalized RRF score for UI
        }

        return results;
    }

    private async Task<IReadOnlyList<DocumentSearchDocumentStatus>> BuildDocumentStatusesAsync(
        IReadOnlyCollection<long> documentIds,
        string? model,
        CancellationToken cancellationToken)
    {
        List<DocumentSearchDocumentStatus> statuses = [];
        foreach (long documentId in documentIds)
        {
            ImportedDocument? document = await documents.GetAsync(documentId, cancellationToken);
            if (document is null)
            {
                continue;
            }

            DocumentEmbeddingStatusSnapshot embeddingStatus =
                await embeddings.GetDocumentEmbeddingStatusAsync(documentId, model, cancellationToken);
            string state = ResolveEmbeddingState(document, model, embeddingStatus);
            statuses.Add(new DocumentSearchDocumentStatus(
                document.Id,
                document.OriginalFileName,
                document.Status,
                document.ChunkCount > 0,
                state,
                embeddingStatus.ChunkCount,
                embeddingStatus.EmbeddedChunkCount));
        }

        return statuses;
    }

    private static string ResolveEmbeddingState(
        ImportedDocument document,
        string? model,
        DocumentEmbeddingStatusSnapshot snapshot)
    {
        if (document.ChunkCount == 0)
        {
            return "NotIndexed";
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return "VectorUnavailable";
        }

        if (snapshot.EmbeddedChunkCount == 0)
        {
            return "NotStarted";
        }

        return snapshot.EmbeddedChunkCount >= snapshot.ChunkCount ? "Complete" : "Partial";
    }

    private sealed record VectorSearchAttempt(
        IReadOnlyList<VectorSearchResult> Results,
        string BackendName);
}
