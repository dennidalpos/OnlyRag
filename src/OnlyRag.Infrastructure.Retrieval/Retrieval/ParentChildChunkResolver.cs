namespace OnlyRag.Infrastructure.Retrieval;

public sealed class ParentChildChunkResolver
{
    private readonly IRetrievalChunkRepository chunkRepository;

    public ParentChildChunkResolver(IRetrievalChunkRepository chunkRepository)
    {
        this.chunkRepository = chunkRepository;
    }

    /// <summary>
    /// Resolves parent content for a batch of child chunks by querying SQLite
    /// when <see cref="SearchChunk.ParentChunkId"/> is set but
    /// <see cref="SearchChunk.ParentContent"/> is missing.
    /// </summary>
    public async Task<IReadOnlyDictionary<long, SearchChunk>> ResolveAllAsync(
        IReadOnlyDictionary<long, SearchChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        // Collect parent chunk IDs that need resolution (ParentChunkId set, ParentContent missing).
        List<long> missingParentIds = [];
        foreach (SearchChunk chunk in chunks.Values)
        {
            if (chunk.ParentChunkId.HasValue
                && string.IsNullOrWhiteSpace(chunk.ParentContent)
                && !missingParentIds.Contains(chunk.ParentChunkId.Value))
            {
                missingParentIds.Add(chunk.ParentChunkId.Value);
            }
        }

        // Batch-fetch all missing parent chunks in one query.
        IReadOnlyDictionary<long, SearchChunk> parentChunks = missingParentIds.Count > 0
            ? await chunkRepository.GetChunksAsync(missingParentIds, cancellationToken)
            : new Dictionary<long, SearchChunk>();

        // Build resolved map.
        Dictionary<long, SearchChunk> resolved = new(chunks.Count);
        foreach (KeyValuePair<long, SearchChunk> kvp in chunks)
        {
            SearchChunk chunk = kvp.Value;

            if (!string.IsNullOrWhiteSpace(chunk.ParentContent))
            {
                // Already has parent content from the JOIN in SqliteRetrievalChunkRepository.
                resolved[kvp.Key] = chunk;
                continue;
            }

            if (chunk.ParentChunkId.HasValue
                && parentChunks.TryGetValue(chunk.ParentChunkId.Value, out SearchChunk? parent))
            {
                // Resolved parent content from database.
                resolved[kvp.Key] = chunk with { ParentContent = parent.Content };
                continue;
            }

            // No parent — use own content as parent content.
            resolved[kvp.Key] = chunk with { ParentContent = chunk.Content };
        }

        return resolved;
    }
}
