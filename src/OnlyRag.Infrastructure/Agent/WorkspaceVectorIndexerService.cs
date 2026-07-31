using OnlyRag.Infrastructure.Logging;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Infrastructure.Agent;

public sealed class WorkspaceVectorIndexerService : IWorkspaceVectorIndexerService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".json", ".md", ".txt", ".xml", ".html", ".css", ".ps1"
    };

    private readonly IQdrantVectorStore? vectorStore;
    private readonly IQueryEmbeddingGenerator? embeddingGenerator;
    private readonly ILoggingService? logger;

    public WorkspaceVectorIndexerService(
        IQdrantVectorStore? vectorStore = null,
        IQueryEmbeddingGenerator? embeddingGenerator = null,
        ILoggingService? logger = null)
    {
        this.vectorStore = vectorStore;
        this.embeddingGenerator = embeddingGenerator;
        this.logger = logger;
    }

    public async Task IndexWorkspaceFileAsync(
        string workspaceRoot,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(relativePath)) return;
        if (vectorStore is null || embeddingGenerator is null) return;

        try
        {
            string ext = Path.GetExtension(relativePath);
            if (!SupportedExtensions.Contains(ext)) return;

            string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, relativePath.TrimStart('/', '\\')));
            if (!File.Exists(fullPath)) return;

            string text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            if (string.IsNullOrWhiteSpace(text) || text.Length > 200_000) return;

            string relPathClean = relativePath.Replace('\\', '/');
            var chunks = ChunkText(text, maxChars: 400);

            for (int i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string chunkContent = $"[File: {relPathClean} | Chunk {i + 1}/{chunks.Count}]\n{chunks[i]}";
                var embedding = await embeddingGenerator.GenerateAsync(chunkContent, cancellationToken);

                if (embedding?.Vector != null && embedding.Vector.Count > 0)
                {
                    long hashChunkId = Math.Abs($"{relPathClean}_chunk_{i}".GetHashCode());
                    long hashDocId = Math.Abs(relPathClean.GetHashCode());

                    await vectorStore.UpsertChunkAsync(
                        chunkId: hashChunkId,
                        documentId: hashDocId,
                        chunkIndex: i,
                        model: "workspace-vector-index",
                        contentHash: relPathClean,
                        vector: embedding.Vector,
                        cancellationToken: cancellationToken);
                }
            }

            logger?.LogInfo("VectorIndexer", $"[REALTIME VECTOR INDEXING] File '{relPathClean}' indicizzato con successo ({chunks.Count} frammenti vettoriali).");
        }
        catch (Exception ex)
        {
            logger?.LogWarning("VectorIndexer", $"Impossibile indicizzare vettorialmente il file '{relativePath}': {ex.Message}");
        }
    }

    private static List<string> ChunkText(string text, int maxChars = 400)
    {
        var list = new List<string>();
        string normalized = text.Replace("\r\n", "\n");
        string[] lines = normalized.Split('\n');

        var currentChunk = new List<string>();
        int currentLength = 0;

        foreach (string line in lines)
        {
            if (currentLength + line.Length > maxChars && currentChunk.Count > 0)
            {
                list.Add(string.Join("\n", currentChunk));
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(line);
            currentLength += line.Length + 1;
        }

        if (currentChunk.Count > 0)
        {
            list.Add(string.Join("\n", currentChunk));
        }

        return list;
    }
}
