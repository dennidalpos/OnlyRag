using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Retrieval;

public sealed class HybridRetrievalService : IHybridRetrievalService
{
    private readonly IDocumentRepository documents;
    private readonly IEmbeddingRepository embeddings;
    private readonly IKeywordSearchService keywordSearch;
    private readonly IVectorSearchService vectorSearch;
    private readonly IRetrievalChunkRepository chunks;
    private readonly IQueryEmbeddingGenerator queryEmbeddingGenerator;
    private readonly HybridRetrievalOptions options;
    private readonly ISettingsRepository? settings;

    public HybridRetrievalService(
        IDocumentRepository documents,
        IEmbeddingRepository embeddings,
        IKeywordSearchService keywordSearch,
        IVectorSearchService vectorSearch,
        IRetrievalChunkRepository chunks,
        IQueryEmbeddingGenerator queryEmbeddingGenerator,
        HybridRetrievalOptions? options = null,
        ISettingsRepository? settings = null)
    {
        this.documents = documents;
        this.embeddings = embeddings;
        this.keywordSearch = keywordSearch;
        this.vectorSearch = vectorSearch;
        this.chunks = chunks;
        this.queryEmbeddingGenerator = queryEmbeddingGenerator;
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

        KeywordSearchResponse keywordResponse = await keywordSearch.SearchAsync(
            query,
            documentIds,
            options.KeywordTopK,
            cancellationToken);

        QueryEmbeddingResult? queryEmbedding = await TryGenerateQueryEmbeddingAsync(query, cancellationToken);
        VectorSearchAttempt vectorSearchAttempt = queryEmbedding is null
            ? new([], $"{vectorSearch.BackendName} (non disponibile per questa query)")
            : await TryVectorSearchAsync(queryEmbedding, documentIds, cancellationToken);

        IReadOnlyList<DocumentSearchResult> results = await MergeResultsAsync(
            query,
            keywordResponse.Results,
            vectorSearchAttempt.Results,
            finalLimit,
            cancellationToken);

        IReadOnlyList<DocumentSearchDocumentStatus> documentStatuses = await BuildDocumentStatusesAsync(
            documentIds,
            queryEmbedding?.Model,
            cancellationToken);

        return new DocumentSearchResponse(
            results,
            documentStatuses,
            keywordResponse.BackendName,
            vectorSearchAttempt.BackendName,
            options.MaxContextCharacters);
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
        CancellationToken cancellationToken)
    {
        try
        {
            QueryEmbeddingResult result = await queryEmbeddingGenerator.GenerateAsync(query, cancellationToken);
            return result.Vector.Count == 0 ? null : result;
        }
        catch (QueryEmbeddingUnavailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<VectorSearchAttempt> TryVectorSearchAsync(
        QueryEmbeddingResult queryEmbedding,
        IReadOnlyCollection<long> documentIds,
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
        catch (InvalidOperationException ex)
        {
            return new VectorSearchAttempt([], $"{vectorSearch.BackendName} ({ex.Message})");
        }
        catch (NotSupportedException ex)
        {
            return new VectorSearchAttempt([], $"{vectorSearch.BackendName} ({ex.Message})");
        }
    }

    private async Task<IReadOnlyList<DocumentSearchResult>> MergeResultsAsync(
        string query,
        IReadOnlyList<KeywordSearchResult> keywordResults,
        IReadOnlyList<VectorSearchResult> vectorResults,
        int finalLimit,
        CancellationToken cancellationToken)
    {
        Dictionary<long, CandidateScore> candidates = [];
        Dictionary<long, double> normalizedKeywordScores = NormalizeKeywordScores(keywordResults);
        foreach (KeywordSearchResult result in keywordResults)
        {
            CandidateScore score = GetOrCreate(candidates, result.ChunkId);
            score.KeywordScore = normalizedKeywordScores[result.ChunkId];
        }

        foreach (VectorSearchResult result in vectorResults)
        {
            CandidateScore score = GetOrCreate(candidates, result.ChunkId);
            score.VectorScore = Math.Clamp((result.Score + 1d) / 2d, 0d, 1d);
        }

        long[] chunkIds = candidates.Keys.ToArray();
        IReadOnlyDictionary<long, SearchChunk> chunkMap = await chunks.GetChunksAsync(chunkIds, cancellationToken);

        List<(SearchChunk Chunk, double Score)> ranked = candidates
            .Where(candidate => chunkMap.ContainsKey(candidate.Key))
            .Select(candidate =>
            {
                SearchChunk chunk = chunkMap[candidate.Key];
                double keywordScore = candidate.Value.KeywordScore ?? 0d;
                double vectorScore = candidate.Value.VectorScore ?? 0d;
                double combined = keywordScore * options.KeywordWeight + vectorScore * options.VectorWeight;
                if (keywordScore > 0d && vectorScore > 0d)
                {
                    combined += Math.Min(keywordScore, vectorScore) * 0.08d;
                }

                return (Chunk: chunk, Score: Math.Clamp(combined, 0d, 1d));
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Chunk.DocumentId)
            .ThenBy(candidate => candidate.Chunk.ChunkIndex)
            .Take(finalLimit)
            .ToList();

        List<DocumentSearchResult> results = [];
        int remainingContextCharacters = Math.Max(0, options.MaxContextCharacters);
        foreach ((SearchChunk chunk, double score) in ranked)
        {
            if (remainingContextCharacters <= 0)
            {
                break;
            }

            string snippet = BuildSnippet(query, chunk.Content, Math.Min(options.SnippetMaxCharacters, remainingContextCharacters));
            if (string.IsNullOrWhiteSpace(snippet))
            {
                continue;
            }

            remainingContextCharacters -= snippet.Length;
            results.Add(new DocumentSearchResult(
                chunk.DocumentId,
                chunk.DocumentName,
                chunk.PageStart,
                chunk.PageEnd,
                chunk.ChunkId,
                snippet,
                Math.Round(score, 4)));
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

    private static CandidateScore GetOrCreate(Dictionary<long, CandidateScore> candidates, long chunkId)
    {
        if (!candidates.TryGetValue(chunkId, out CandidateScore? score))
        {
            score = new CandidateScore();
            candidates[chunkId] = score;
        }

        return score;
    }

    private static Dictionary<long, double> NormalizeKeywordScores(IReadOnlyList<KeywordSearchResult> results)
    {
        if (results.Count == 0)
        {
            return [];
        }

        double max = results.Max(result => result.Score);
        double min = results.Min(result => result.Score);
        Dictionary<long, double> normalized = [];
        foreach (KeywordSearchResult result in results)
        {
            normalized[result.ChunkId] = Math.Abs(max - min) < double.Epsilon
                ? 1d
                : Math.Clamp((result.Score - min) / (max - min), 0d, 1d);
        }

        return normalized;
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

    private static string BuildSnippet(string query, string content, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        string normalizedContent = NormalizeWhitespace(content);
        if (normalizedContent.Length <= maxLength)
        {
            return normalizedContent;
        }

        int firstMatch = FindFirstTermIndex(query, normalizedContent);
        int start = firstMatch < 0
            ? 0
            : Math.Max(0, firstMatch - maxLength / 3);
        if (start + maxLength > normalizedContent.Length)
        {
            start = Math.Max(0, normalizedContent.Length - maxLength);
        }

        string snippet = normalizedContent.Substring(start, Math.Min(maxLength, normalizedContent.Length - start)).Trim();
        if (start > 0)
        {
            snippet = "..." + snippet;
        }

        if (start + maxLength < normalizedContent.Length)
        {
            snippet += "...";
        }

        return snippet;
    }

    private static int FindFirstTermIndex(string query, string content)
    {
        foreach (string term in query.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int index = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(" ", value.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private sealed class CandidateScore
    {
        public double? KeywordScore { get; set; }

        public double? VectorScore { get; set; }
    }

    private sealed record VectorSearchAttempt(
        IReadOnlyList<VectorSearchResult> Results,
        string BackendName);
}
