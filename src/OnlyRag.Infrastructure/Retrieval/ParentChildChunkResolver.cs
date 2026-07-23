namespace OnlyRag.Infrastructure.Retrieval;

public sealed class ParentChildChunkResolver
{
    public SearchChunk Resolve(SearchChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (!string.IsNullOrWhiteSpace(chunk.ParentContent))
        {
            return chunk;
        }

        return chunk with { ParentContent = chunk.Content };
    }
}
