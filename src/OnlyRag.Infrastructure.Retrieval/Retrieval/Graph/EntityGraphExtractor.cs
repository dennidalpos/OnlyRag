using System.Text.RegularExpressions;
using OnlyRag.Core;

namespace OnlyRag.Infrastructure.Retrieval.Graph;

public interface IEntityGraphExtractor
{
    (IReadOnlyList<EntityGraphNode> Nodes, IReadOnlyList<EntityGraphEdge> Edges) ExtractGraph(
        long documentId,
        long chunkId,
        string chunkContent);
}

public sealed class EntityGraphExtractor : IEntityGraphExtractor
{
    private static readonly Regex EntityRegex = new(
        @"\b([A-Z][a-zA-Z0-9_]+(?:\s+[A-Z][a-zA-Z0-9_]+)*)\b",
        RegexOptions.Compiled);

    private static readonly (string Relation, Regex Pattern)[] RelationPatterns = new[]
    {
        ("uses", new Regex(@"\b([A-Z][a-zA-Z0-9_]+)\s+(?:uses|utilizes|employs|for)\s+([A-Z][a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("depends_on", new Regex(@"\b([A-Z][a-zA-Z0-9_]+)\s+(?:depends on|requires|relies on)\s+([A-Z][a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("is_a", new Regex(@"\b([A-Z][a-zA-Z0-9_]+)\s+(?:is a|is an|type of)\s+([A-Z][a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("belongs_to", new Regex(@"\b([A-Z][a-zA-Z0-9_]+)\s+(?:belongs to|part of)\s+([A-Z][a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("connects_to", new Regex(@"\b([A-Z][a-zA-Z0-9_]+)\s+(?:connects to|interacts with|relates to)\s+([A-Z][a-zA-Z0-9_]+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled))
    };

    public (IReadOnlyList<EntityGraphNode> Nodes, IReadOnlyList<EntityGraphEdge> Edges) ExtractGraph(
        long documentId,
        long chunkId,
        string chunkContent)
    {
        if (string.IsNullOrWhiteSpace(chunkContent))
        {
            return (Array.Empty<EntityGraphNode>(), Array.Empty<EntityGraphEdge>());
        }

        var nodesDict = new Dictionary<string, EntityGraphNode>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<EntityGraphEdge>();

        var matches = EntityRegex.Matches(chunkContent);
        foreach (Match match in matches)
        {
            string entityName = match.Value.Trim();
            if (entityName.Length < 3 || IsStopword(entityName)) continue;

            if (!nodesDict.ContainsKey(entityName))
            {
                string nodeId = $"node_{Guid.NewGuid():N}"[..12];
                string type = InferEntityType(entityName);
                nodesDict[entityName] = new EntityGraphNode(
                    NodeId: nodeId,
                    DocumentId: documentId.ToString(),
                    ChunkId: chunkId.ToString(),
                    Name: entityName,
                    Type: type,
                    Description: $"Extracted entity '{entityName}' from chunk {chunkId}");
            }
        }

        foreach (var (relation, pattern) in RelationPatterns)
        {
            var relMatches = pattern.Matches(chunkContent);
            foreach (Match relMatch in relMatches)
            {
                if (relMatch.Groups.Count >= 3)
                {
                    string sourceName = relMatch.Groups[1].Value.Trim();
                    string targetName = relMatch.Groups[2].Value.Trim();

                    if (nodesDict.TryGetValue(sourceName, out var sourceNode) &&
                        nodesDict.TryGetValue(targetName, out var targetNode) &&
                        !sourceNode.NodeId.Equals(targetNode.NodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        string edgeId = $"edge_{Guid.NewGuid():N}"[..12];
                        edges.Add(new EntityGraphEdge(
                            EdgeId: edgeId,
                            SourceNodeId: sourceNode.NodeId,
                            TargetNodeId: targetNode.NodeId,
                            RelationType: relation,
                            Weight: 1.0f,
                            ChunkId: chunkId.ToString()));
                    }
                }
            }
        }

        return (nodesDict.Values.ToList(), edges);
    }

    private static string InferEntityType(string entityName)
    {
        if (entityName.EndsWith("Service", StringComparison.OrdinalIgnoreCase) ||
            entityName.EndsWith("Engine", StringComparison.OrdinalIgnoreCase) ||
            entityName.EndsWith("Manager", StringComparison.OrdinalIgnoreCase) ||
            entityName.EndsWith("Store", StringComparison.OrdinalIgnoreCase) ||
            entityName.EndsWith("Client", StringComparison.OrdinalIgnoreCase))
        {
            return "Component";
        }

        if (entityName.Equals("SQLite", StringComparison.OrdinalIgnoreCase) ||
            entityName.Equals("Qdrant", StringComparison.OrdinalIgnoreCase) ||
            entityName.Equals("Ollama", StringComparison.OrdinalIgnoreCase) ||
            entityName.Equals("ONNX", StringComparison.OrdinalIgnoreCase) ||
            entityName.Equals("PaddleOCR", StringComparison.OrdinalIgnoreCase))
        {
            return "Technology";
        }

        return "Concept";
    }

    private static bool IsStopword(string word)
    {
        return word is "The" or "This" or "That" or "With" or "From" or "Each" or "Some" or "When" or "Where" or "What" or "Only";
    }
}
