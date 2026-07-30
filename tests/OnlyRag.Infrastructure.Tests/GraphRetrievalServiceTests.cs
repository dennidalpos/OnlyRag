using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval.Graph;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class GraphRetrievalServiceTests : IDisposable
{
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly LocalSqliteSchemaInitializer schemaInitializer;

    public GraphRetrievalServiceTests()
    {
        string testRootDir = Path.Combine(Path.GetTempPath(), $"onlyrag_graph_root_{Guid.NewGuid():N}");
        var paths = AppStoragePaths.FromRoot(testRootDir);
        descriptor = new LocalSqliteStoreDescriptor(paths);
        connectionFactory = new LocalSqliteConnectionFactory(descriptor);
        schemaInitializer = new LocalSqliteSchemaInitializer(descriptor, connectionFactory);
        schemaInitializer.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(descriptor.Paths.DataRoot)) Directory.Delete(descriptor.Paths.DataRoot, true);
        }
        catch { }
    }

    [Fact]
    public void EntityGraphExtractor_Extracts_Nodes_And_Edges()
    {
        var extractor = new EntityGraphExtractor();
        string text = "OllamaEngine uses QdrantStore for vector search. QdrantStore connects to SQLite.";

        var (nodes, edges) = extractor.ExtractGraph(1, 10, text);

        Assert.NotEmpty(nodes);
        Assert.Contains(nodes, n => n.Name.Equals("OllamaEngine", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nodes, n => n.Name.Equals("QdrantStore", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(nodes, n => n.Name.Equals("SQLite", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SqliteGraphRetrievalService_Inserts_And_Searches_MultiHop()
    {
        var graphService = new SqliteGraphRetrievalService(connectionFactory);

        var nodes = new List<EntityGraphNode>
        {
            new("n1", "1", "10", "OllamaEngine", "Component", "Ollama LLM Engine"),
            new("n2", "1", "10", "QdrantStore", "Component", "Qdrant Vector Database"),
            new("n3", "1", "11", "SqliteStore", "Database", "SQLite Local Database")
        };

        var edges = new List<EntityGraphEdge>
        {
            new("e1", "n1", "n2", "uses", 1.0f, "10"),
            new("e2", "n2", "n3", "connects_to", 1.0f, "11")
        };

        await graphService.InsertGraphAsync(nodes, edges);

        var result = await graphService.SearchGraphAsync("OllamaEngine", maxHops: 2, maxNodes: 10);

        Assert.NotEmpty(result.Nodes);
        Assert.NotEmpty(result.Edges);
        Assert.True(result.RelevanceScore > 0f);
    }
}
