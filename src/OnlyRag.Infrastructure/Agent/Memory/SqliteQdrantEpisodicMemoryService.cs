using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Retrieval;
using OnlyRag.Infrastructure.Storage;
using OnlyRag.Infrastructure.Vector;

namespace OnlyRag.Infrastructure.Agent.Memory;

public sealed class SqliteQdrantEpisodicMemoryService : IAgentEpisodicMemoryService
{
    private static readonly char[] Separators = new[] { ' ', ',', '.', ';', '?', '!' };

    private readonly ISqliteConnectionFactory connectionFactory;
    private readonly IQdrantVectorStore? vectorStore;
    private readonly IQueryEmbeddingGenerator? embeddingGenerator;

    public SqliteQdrantEpisodicMemoryService(
        ISqliteConnectionFactory connectionFactory,
        IQdrantVectorStore? vectorStore = null,
        IQueryEmbeddingGenerator? embeddingGenerator = null)
    {
        this.connectionFactory = connectionFactory;
        this.vectorStore = vectorStore;
        this.embeddingGenerator = embeddingGenerator;
    }

    public async Task SaveMemoryAsync(
        AgentEpisodicMemory memory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(memory.SessionId) || string.IsNullOrWhiteSpace(memory.Goal)) return;

        string keyFactsJson = JsonSerializer.Serialize(memory.KeyFacts ?? Array.Empty<string>());
        string qdrantPointId = $"mem_{memory.SessionId}";

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO agent_episodic_memories (session_id, goal, summary, key_facts_json, qdrant_point_id, created_at_utc)
            VALUES ($sessionId, $goal, $summary, $keyFacts, $pointId, $createdAt);
            """;
        cmd.AddParameter("$sessionId", memory.SessionId);
        cmd.AddParameter("$goal", memory.Goal);
        cmd.AddParameter("$summary", memory.Summary);
        cmd.AddParameter("$keyFacts", keyFactsJson);
        cmd.AddParameter("$pointId", qdrantPointId);
        cmd.AddParameter("$createdAt", memory.Timestamp.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        if (vectorStore != null && embeddingGenerator != null)
        {
            try
            {
                var result = await embeddingGenerator.GenerateAsync($"{memory.Goal}\n{memory.Summary}", cancellationToken);
                if (result?.Vector != null && result.Vector.Count > 0)
                {
                    long hashId = BuildStablePointId(memory.SessionId);
                    await vectorStore.UpsertChunkAsync(
                        chunkId: hashId,
                        documentId: 0,
                        chunkIndex: 0,
                        model: "agent-episodic-memory",
                        contentHash: memory.SessionId,
                        vector: result.Vector,
                        cancellationToken: cancellationToken);
                }
            }
            catch
            {
                // Vector storage failure is non-blocking; SQLite record is preserved.
            }
        }
    }

    public async Task<IReadOnlyList<AgentEpisodicMemory>> SearchRelevantMemoriesAsync(
        string currentGoal,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(currentGoal))
        {
            return Array.Empty<AgentEpisodicMemory>();
        }

        var results = new List<AgentEpisodicMemory>();
        var seenSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Vector similarity search in Qdrant if vectorStore & embeddingGenerator available
        if (vectorStore != null && embeddingGenerator != null)
        {
            try
            {
                var embedRes = await embeddingGenerator.GenerateAsync(currentGoal, cancellationToken);
                if (embedRes?.Vector != null && embedRes.Vector.Count > 0)
                {
                    var vectorMatches = await vectorStore.SearchAsync("agent-episodic-memory", embedRes.Vector, Array.Empty<long>(), topK, cancellationToken);
                    if (vectorMatches.Count > 0)
                    {
                        var matchedHashIds = vectorMatches.Select(m => m.ChunkId).ToHashSet();
                        await using SqliteConnection conn = await connectionFactory.OpenConnectionAsync(cancellationToken);
                        await using SqliteCommand cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT session_id, goal, summary, key_facts_json, created_at_utc FROM agent_episodic_memories ORDER BY id DESC;";
                        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var mem = ParseMemory(reader);
                            long hashId = BuildStablePointId(mem.SessionId);
                            if (matchedHashIds.Contains(hashId) && seenSessionIds.Add(mem.SessionId))
                            {
                                results.Add(mem);
                            }

                        }
                    }
                }
            }
            catch
            {
                // Vector search failure falls back to keyword SQLite search.
            }
        }

        // 2. Keyword fallback / augmentation via SQLite LIKE queries
        if (results.Count < topK)
        {
            await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

            var words = currentGoal.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                                   .Where(w => w.Length >= 3)
                                   .Take(4)
                                   .ToList();

            if (words.Count == 0)
            {
                await using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = """
                    SELECT session_id, goal, summary, key_facts_json, created_at_utc
                    FROM agent_episodic_memories
                    ORDER BY id DESC
                    LIMIT $limit;
                    """;
                cmd.AddParameter("$limit", topK);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var mem = ParseMemory(reader);
                    if (seenSessionIds.Add(mem.SessionId))
                    {
                        results.Add(mem);
                    }
                }
            }
            else
            {
                foreach (string word in words)
                {
                    await using SqliteCommand cmd = connection.CreateCommand();
                    cmd.CommandText = """
                        SELECT session_id, goal, summary, key_facts_json, created_at_utc
                        FROM agent_episodic_memories
                        WHERE goal LIKE $pattern OR summary LIKE $pattern
                        ORDER BY id DESC
                        LIMIT $limit;
                        """;
                    cmd.AddParameter("$pattern", $"%{word}%");
                    cmd.AddParameter("$limit", topK);

                    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var mem = ParseMemory(reader);
                        if (seenSessionIds.Add(mem.SessionId))
                        {
                            results.Add(mem);
                        }
                    }
                }
            }
        }

        return results.Take(topK).ToList();
    }

    private static long BuildStablePointId(string sessionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        long value = BitConverter.ToInt64(hash, 0) & long.MaxValue;
        return value == 0 ? 1 : value;
    }

    private static AgentEpisodicMemory ParseMemory(SqliteDataReader reader)
    {
        string sessionId = reader.GetString(0);
        string goal = reader.GetString(1);
        string summary = reader.GetString(2);
        string keyFactsJson = reader.GetString(3);
        DateTimeOffset createdAt = DateTimeOffset.TryParse(reader.GetString(4), out var dt) ? dt : DateTimeOffset.UtcNow;

        var keyFacts = new List<string>();
        try
        {
            keyFacts = JsonSerializer.Deserialize<List<string>>(keyFactsJson) ?? new List<string>();
        }
        catch { }

        return new AgentEpisodicMemory(sessionId, goal, summary, keyFacts, createdAt);
    }
}
