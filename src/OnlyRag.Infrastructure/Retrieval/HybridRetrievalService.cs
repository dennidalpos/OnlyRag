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
    private readonly CragDecisionEngine cragEvaluator;
    private readonly IQueryIntentClassifierService queryIntentClassifier;
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
        CragDecisionEngine? cragEvaluator = null,
        IQueryIntentClassifierService? queryIntentClassifier = null,
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
        this.parentChildResolver = parentChildResolver ?? new ParentChildChunkResolver(chunks);
        this.cragEvaluator = cragEvaluator ?? new CragDecisionEngine();
        this.queryIntentClassifier = queryIntentClassifier ?? new QueryIntentClassifierService();
        this.options = options ?? HybridRetrievalOptions.Default;
        this.settings = settings;
    }

    public Task<DocumentSearchResponse> SearchAsync(
        DocumentSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        return SearchAsync(request, false, cancellationToken);
    }

    public async Task<DocumentSearchResponse> SearchAsync(
        DocumentSearchRequest request,
        bool isReformulation,
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

        // ── Stage 1: Query Transformation ──────────────────────────────────
        QueryTransformationStrategy configuredStrategy = await GetTransformationStrategyAsync(cancellationToken);
        QueryTransformationResult transformation = await queryTransformer.TransformAsync(
            query,
            configuredStrategy,
            cancellationToken);

        IReadOnlyList<string> searchQueries = transformation.ExpandedQueries.Count > 0
            ? transformation.ExpandedQueries
            : [query];

        var pipelineResult = await ExecuteStages2To5Async(query, searchQueries, documentIds, finalLimit, notices, cancellationToken);
        var finalResults = pipelineResult.Results;

        // ── Stage 6: CRAG Confidence Check ─────────────────────────────────
        double cragThreshold = await GetCragThresholdAsync(cancellationToken);
        CragDecision cragResult = await cragEvaluator.EvaluateAsync(finalResults, query, cragThreshold, cancellationToken: cancellationToken);

        if (cragResult.Action == CragAction.Abstain)
        {
            notices.Add(new RetrievalNotice("crag_insufficient_evidence", cragResult.SummaryNotice));
        }
        else if (cragResult.Action == CragAction.Reformulate)
        {
            if (isReformulation)
            {
                notices.Add(new RetrievalNotice("crag_insufficient_evidence", cragResult.SummaryNotice));
            }
            else
            {
                notices.Add(new RetrievalNotice("crag_reformulation", cragResult.SummaryNotice));

                var reformQueries = cragResult.ReformulatedQueries ?? [];
                string primaryReformQuery = reformQueries.Count > 0 ? reformQueries[0] : query;
                var refPipelineResult = await ExecuteStages2To5Async(primaryReformQuery, reformQueries, documentIds, finalLimit, notices, cancellationToken);

                var mergedResults = finalResults.Concat(refPipelineResult.Results)
                    .DistinctBy(r => r.ChunkId)
                    .ToList();

                List<ReRankCandidate> mergedCandidates = mergedResults.Select(m => new ReRankCandidate(m.ChunkId, m.Snippet)).ToList();
                var mergedReRankedScores = await reRanker.ReRankAsync(query, mergedCandidates, cancellationToken);
                var mergedScoreMap = mergedReRankedScores.ToDictionary(s => s.ChunkId, s => s.Score);

                finalResults = mergedResults.Select(m =>
                {
                    double rs = mergedScoreMap.TryGetValue(m.ChunkId, out var s) ? s : m.Score;
                    return m with { Score = rs, ReRankScore = rs };
                }).OrderByDescending(m => m.ReRankScore ?? m.Score).Take(finalLimit).ToList();
            }
        }

        IReadOnlyList<DocumentSearchDocumentStatus> documentStatuses = await BuildDocumentStatusesAsync(
            documentIds,
            pipelineResult.QueryEmbedding?.Model,
            cancellationToken);

        return new DocumentSearchResponse(
            finalResults,
            documentStatuses,
            pipelineResult.KeywordBackendName,
            pipelineResult.VectorAttempt.BackendName,
            options.MaxContextCharacters)
        {
            Notices = notices
        };
    }

    private async Task<(IReadOnlyList<DocumentSearchResult> Results, VectorSearchAttempt VectorAttempt, string KeywordBackendName, QueryEmbeddingResult? QueryEmbedding)> ExecuteStages2To5Async(
        string query,
        IReadOnlyList<string> searchQueries,
        long[] documentIds,
        int finalLimit,
        List<RetrievalNotice> notices,
        CancellationToken cancellationToken)
    {
        // ── Stage 2: Coarse Hybrid Retrieval (FTS5 + Qdrant in parallel) ───
        Task<QueryEmbeddingResult?> embeddingTask = TryGenerateQueryEmbeddingAsync(query, notices, cancellationToken);

        List<Task<KeywordSearchResponse>> keywordTasks = [];
        foreach (string q in searchQueries)
        {
            keywordTasks.Add(keywordSearch.SearchAsync(q, documentIds, options.KeywordTopK, cancellationToken));
        }

        await Task.WhenAll([.. keywordTasks, embeddingTask]);

        QueryEmbeddingResult? queryEmbedding = await embeddingTask;

        VectorSearchAttempt vectorSearchAttempt = queryEmbedding is null
            ? new VectorSearchAttempt([], "Qdrant unavailable")
            : await TryVectorSearchAsync(queryEmbedding, documentIds, notices, cancellationToken);

        List<KeywordSearchResult> allKeywordResults = [];
        string keywordBackendName = "none";
        foreach (Task<KeywordSearchResponse> task in keywordTasks)
        {
            KeywordSearchResponse response = await task;
            allKeywordResults.AddRange(response.Results);
            if (keywordBackendName == "none" || keywordBackendName == "SQLite FTS5")
            {
                keywordBackendName = response.BackendName;
            }
        }

        // ── Stage 3: Reciprocal Rank Fusion (RRF) ──────────────────────────
        IReadOnlyList<DocumentSearchResult> coarseResults = await RrfMergeResultsAsync(
            query,
            allKeywordResults,
            vectorSearchAttempt.Results,
            options.VectorTopK,
            cancellationToken);

        // ── Stage 4: 2nd-Stage Re-ranking on child chunks (Intent Adaptive) ──
        QueryIntentClassificationResult intent = queryIntentClassifier.ClassifyIntent(query);
        List<ReRankCandidate> candidates = [];
        foreach (DocumentSearchResult coarse in coarseResults)
        {
            candidates.Add(new ReRankCandidate(coarse.ChunkId, coarse.Snippet));
        }

        IReadOnlyList<ReRankResult> reRankedScores = await reRanker.ReRankAsync(query, candidates, cancellationToken);
        Dictionary<long, double> scoreMap = reRankedScores.ToDictionary(s => s.ChunkId, s => s.Score);

        List<DocumentSearchResult> reRanked = [];
        foreach (DocumentSearchResult coarse in coarseResults)
        {
            double reRankScore = scoreMap.TryGetValue(coarse.ChunkId, out double score) ? score : coarse.Score;
            reRanked.Add(coarse with { Score = reRankScore, ReRankScore = reRankScore });
        }

        var filteredByThreshold = reRanked.Where(r => (r.ReRankScore ?? r.Score) >= intent.MinimumRerankScoreThreshold).ToList();
        IReadOnlyList<DocumentSearchResult> topK = (filteredByThreshold.Count > 0 ? filteredByThreshold : reRanked)
            .OrderByDescending(r => r.ReRankScore ?? r.Score)
            .Take(finalLimit)
            .ToList();

        // ── Stage 5: Parent-Child Resolution (only on top-K) ───────────────
        long[] topKChunkIds = topK.Select(r => r.ChunkId).ToArray();
        IReadOnlyDictionary<long, SearchChunk> rawChunkMap = await chunks.GetChunksAsync(topKChunkIds, cancellationToken);
        IReadOnlyDictionary<long, SearchChunk> resolvedChunkMap =
            await parentChildResolver.ResolveAllAsync(rawChunkMap, cancellationToken);

        List<DocumentSearchResult> finalResults = [];
        foreach (DocumentSearchResult result in topK)
        {
            SearchChunk? resolved = resolvedChunkMap.TryGetValue(result.ChunkId, out SearchChunk? r) ? r : null;
            finalResults.Add(result with
            {
                ParentContent = resolved?.ParentContent ?? result.Snippet,
                SectionHeading = resolved?.SectionHeading,
                ChunkLevel = resolved?.ChunkLevel ?? "Child"
            });
        }

        return (finalResults, vectorSearchAttempt, keywordBackendName, queryEmbedding);
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

    private async Task<QueryTransformationStrategy> GetTransformationStrategyAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return QueryTransformationStrategy.MultiQuery;
        }

        string? value = await settings.GetValueAsync("retrieval.transformationStrategy", cancellationToken);
        return Enum.TryParse<QueryTransformationStrategy>(value, ignoreCase: true, out var parsed)
            ? parsed
            : QueryTransformationStrategy.MultiQuery;
    }

    private async Task<double> GetCragThresholdAsync(CancellationToken cancellationToken)
    {
        if (settings is null)
        {
            return options.CragConfidenceThreshold;
        }

        string? value = await settings.GetValueAsync("retrieval.cragConfidenceThreshold", cancellationToken);
        return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? Math.Clamp(parsed, 0.01, 0.99)
            : options.CragConfidenceThreshold;
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
        double k = options.RrfK;
        double wKey = options.KeywordWeight;
        double wVec = options.VectorWeight;

        if (settings != null)
        {
            string? rrfKVal = await settings.GetValueAsync("retrieval.rrfK", cancellationToken);
            if (double.TryParse(rrfKVal, System.Globalization.CultureInfo.InvariantCulture, out double parsedK))
            {
                k = Math.Clamp(parsedK, 1d, 100d);
            }

            string? wKeyVal = await settings.GetValueAsync("retrieval.keywordWeight", cancellationToken);
            if (double.TryParse(wKeyVal, System.Globalization.CultureInfo.InvariantCulture, out double parsedWKey))
            {
                wKey = Math.Clamp(parsedWKey, 0.05, 0.95);
            }

            string? wVecVal = await settings.GetValueAsync("retrieval.vectorWeight", cancellationToken);
            if (double.TryParse(wVecVal, System.Globalization.CultureInfo.InvariantCulture, out double parsedWVec))
            {
                wVec = Math.Clamp(parsedWVec, 0.05, 0.95);
            }
        }

        Dictionary<long, double> rrfScores = [];

        for (int rank = 0; rank < keywordResults.Count; rank++)
        {
            long chunkId = keywordResults[rank].ChunkId;
            rrfScores[chunkId] = rrfScores.GetValueOrDefault(chunkId, 0d) + (wKey / (k + rank + 1));
        }

        for (int rank = 0; rank < vectorResults.Count; rank++)
        {
            long chunkId = vectorResults[rank].ChunkId;
            rrfScores[chunkId] = rrfScores.GetValueOrDefault(chunkId, 0d) + (wVec / (k + rank + 1));
        }

        // Max possible RRF score under given weights for normalization
        double maxPossibleScore = (wKey / (k + 1)) + (wVec / (k + 1));
        double maxRrfScore = rrfScores.Count > 0 ? rrfScores.Values.Max() : maxPossibleScore;
        if (maxRrfScore <= 0d) maxRrfScore = maxPossibleScore;

        long[] chunkIds = rrfScores.Keys.ToArray();
        IReadOnlyDictionary<long, SearchChunk> chunkMap = await chunks.GetChunksAsync(chunkIds, cancellationToken);

        List<DocumentSearchResult> results = [];
        foreach (KeyValuePair<long, double> kvp in rrfScores.OrderByDescending(kv => kv.Value).Take(limit))
        {
            if (!chunkMap.TryGetValue(kvp.Key, out SearchChunk? chunk))
            {
                continue;
            }

            double normalizedScore = Math.Round(Math.Clamp(kvp.Value / maxRrfScore, 0d, 1d), 4);
            results.Add(new DocumentSearchResult(
                chunk.DocumentId,
                chunk.DocumentName,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.ChunkId,
                chunk.Content,
                normalizedScore));
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
