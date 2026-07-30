using OnlyRag.Core;
using OnlyRag.Infrastructure.Agent.Memory;
using OnlyRag.Infrastructure.Storage;
using Xunit;

namespace OnlyRag.Infrastructure.Tests;

public sealed class EpisodicMemoryServiceTests : IDisposable
{
    private readonly LocalSqliteStoreDescriptor descriptor;
    private readonly LocalSqliteConnectionFactory connectionFactory;
    private readonly LocalSqliteSchemaInitializer schemaInitializer;

    public EpisodicMemoryServiceTests()
    {
        string testRootDir = Path.Combine(Path.GetTempPath(), $"onlyrag_mem_root_{Guid.NewGuid():N}");
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
    public async Task SqliteQdrantEpisodicMemoryService_Saves_And_Recalls_Memories()
    {
        var memService = new SqliteQdrantEpisodicMemoryService(connectionFactory);

        var mem1 = new AgentEpisodicMemory(
            SessionId: "s1",
            Goal: "Refactor SQLite Schema for GraphRAG",
            Summary: "Created document_graph_nodes and document_graph_edges tables in schema v3",
            KeyFacts: new[] { "Schema v3 applied", "FTS5 indexes added" },
            Timestamp: DateTimeOffset.UtcNow);

        await memService.SaveMemoryAsync(mem1);

        var recalled = await memService.SearchRelevantMemoriesAsync("GraphRAG SQLite", topK: 5);

        Assert.NotEmpty(recalled);
        Assert.Equal("s1", recalled[0].SessionId);
        Assert.Equal("Refactor SQLite Schema for GraphRAG", recalled[0].Goal);
    }
}
