using Microsoft.Data.Sqlite;
using OnlyRag.Core;
using OnlyRag.Infrastructure.Storage;

namespace OnlyRag.Infrastructure.Retrieval.Graph;

public sealed class SqliteGraphRetrievalService : IGraphRetrievalService
{
    private static readonly char[] Separators = new[] { ' ', ',', '.', ';', '?', '!' };

    private readonly ISqliteConnectionFactory connectionFactory;

    public SqliteGraphRetrievalService(ISqliteConnectionFactory connectionFactory)
    {
        this.connectionFactory = connectionFactory;
    }

    public async Task InsertGraphAsync(
        IReadOnlyList<EntityGraphNode> nodes,
        IReadOnlyList<EntityGraphEdge> edges,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0 && edges.Count == 0) return;

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var nodeDbIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in nodes)
            {
                await using SqliteCommand cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO document_graph_nodes (node_uid, document_id, chunk_id, name, type, description, created_at_utc)
                    VALUES ($uid, $docId, $chunkId, $name, $type, $desc, $createdAt)
                    ON CONFLICT(node_uid) DO UPDATE SET description = excluded.description
                    RETURNING id;
                    """;
                long dIdVal = long.TryParse(node.DocumentId, out long parsedDocId) && parsedDocId > 0 ? parsedDocId : 0;
                bool docExists = false;
                if (dIdVal > 0)
                {
                    await using var checkDocCmd = connection.CreateCommand();
                    checkDocCmd.Transaction = transaction;
                    checkDocCmd.CommandText = "SELECT 1 FROM documents WHERE id = $id LIMIT 1;";
                    checkDocCmd.AddParameter("$id", dIdVal);
                    docExists = (await checkDocCmd.ExecuteScalarAsync(cancellationToken)) != null;
                }

                cmd.AddParameter("$uid", node.NodeId);
                cmd.AddParameter("$docId", docExists ? dIdVal : DBNull.Value);
                cmd.AddParameter("$chunkId", long.TryParse(node.ChunkId, out long cId) ? cId : DBNull.Value);
                cmd.AddParameter("$name", node.Name);
                cmd.AddParameter("$type", node.Type);
                cmd.AddParameter("$desc", (object?)node.Description ?? DBNull.Value);
                cmd.AddParameter("$createdAt", DateTimeOffset.UtcNow.ToString("o"));

                object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                long nodeDbId = 0;
                if (scalar != null && scalar != DBNull.Value)
                {
                    nodeDbId = Convert.ToInt64(scalar);
                }
                else
                {
                    await using SqliteCommand selectCmd = connection.CreateCommand();
                    selectCmd.Transaction = transaction;
                    selectCmd.CommandText = "SELECT id FROM document_graph_nodes WHERE node_uid = $uid;";
                    selectCmd.AddParameter("$uid", node.NodeId);
                    object? s = await selectCmd.ExecuteScalarAsync(cancellationToken);
                    if (s != null && s != DBNull.Value) nodeDbId = Convert.ToInt64(s);
                }

                if (nodeDbId > 0)
                {
                    nodeDbIds[node.NodeId] = nodeDbId;
                }
            }

            foreach (var edge in edges)
            {
                if (!nodeDbIds.TryGetValue(edge.SourceNodeId, out long srcId) ||
                    !nodeDbIds.TryGetValue(edge.TargetNodeId, out long tgtId))
                {
                    continue;
                }

                await using SqliteCommand cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT INTO document_graph_edges (edge_uid, source_node_id, target_node_id, relation_type, weight, chunk_id, created_at_utc)
                    VALUES ($uid, $srcId, $tgtId, $rel, $weight, $chunkId, $createdAt)
                    ON CONFLICT(edge_uid) DO NOTHING;
                    """;
                cmd.AddParameter("$uid", edge.EdgeId);
                cmd.AddParameter("$srcId", srcId);
                cmd.AddParameter("$tgtId", tgtId);
                cmd.AddParameter("$rel", edge.RelationType);
                cmd.AddParameter("$weight", edge.Weight);
                cmd.AddParameter("$chunkId", long.TryParse(edge.ChunkId, out long cId) ? cId : DBNull.Value);
                cmd.AddParameter("$createdAt", DateTimeOffset.UtcNow.ToString("o"));

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<GraphRetrievalResult> SearchGraphAsync(
        string query,
        int maxHops = 2,
        int maxNodes = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new GraphRetrievalResult(Array.Empty<EntityGraphNode>(), Array.Empty<EntityGraphEdge>(), Array.Empty<string>(), 0f);
        }

        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var words = query.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => w.Length >= 3)
                         .Take(5)
                         .ToList();

        if (words.Count == 0)
        {
            return new GraphRetrievalResult(Array.Empty<EntityGraphNode>(), Array.Empty<EntityGraphEdge>(), Array.Empty<string>(), 0f);
        }

        var matchedNodes = new Dictionary<long, EntityGraphNode>();
        var matchedEdges = new List<EntityGraphEdge>();
        var relatedChunkIds = new HashSet<string>();

        // 1. Initial entity node search
        foreach (string word in words)
        {
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT id, node_uid, document_id, chunk_id, name, type, description
                FROM document_graph_nodes
                WHERE name LIKE $pattern
                LIMIT $limit;
                """;
            cmd.AddParameter("$pattern", $"%{word}%");
            cmd.AddParameter("$limit", maxNodes);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long dbId = reader.GetInt64(0);
                string nodeUid = reader.GetString(1);
                long docId = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                string? chunkId = reader.IsDBNull(3) ? null : reader.GetInt64(3).ToString();
                string name = reader.GetString(4);
                string type = reader.GetString(5);
                string? desc = reader.IsDBNull(6) ? null : reader.GetString(6);

                if (!matchedNodes.ContainsKey(dbId))
                {
                    matchedNodes[dbId] = new EntityGraphNode(nodeUid, docId > 0 ? docId.ToString() : "", chunkId ?? "", name, type, desc ?? "");
                    if (!string.IsNullOrEmpty(chunkId)) relatedChunkIds.Add(chunkId);
                }
            }
        }

        if (matchedNodes.Count == 0)
        {
            return new GraphRetrievalResult(Array.Empty<EntityGraphNode>(), Array.Empty<EntityGraphEdge>(), Array.Empty<string>(), 0f);
        }

        // 2. Multi-hop edge traversal
        var currentHopNodeIds = matchedNodes.Keys.ToList();

        for (int hop = 1; hop <= maxHops && currentHopNodeIds.Count > 0; hop++)
        {
            string inClause = string.Join(",", currentHopNodeIds);
            await using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT e.edge_uid, e.source_node_id, e.target_node_id, e.relation_type, e.weight, e.chunk_id,
                       sn.node_uid as src_uid, sn.document_id as src_doc, sn.name as src_name, sn.type as src_type,
                       tn.node_uid as tgt_uid, tn.document_id as tgt_doc, tn.name as tgt_name, tn.type as tgt_type
                FROM document_graph_edges e
                JOIN document_graph_nodes sn ON e.source_node_id = sn.id
                JOIN document_graph_nodes tn ON e.target_node_id = tn.id
                WHERE e.source_node_id IN ({inClause}) OR e.target_node_id IN ({inClause})
                LIMIT $limit;
                """;
            cmd.AddParameter("$limit", maxNodes * 2);

            var nextHopNodeIds = new List<long>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string edgeUid = reader.GetString(0);
                long srcDbId = reader.GetInt64(1);
                long tgtDbId = reader.GetInt64(2);
                string relType = reader.GetString(3);
                float weight = (float)reader.GetDouble(4);
                string? chunkId = reader.IsDBNull(5) ? null : reader.GetInt64(5).ToString();

                string srcUid = reader.GetString(6);
                long srcDoc = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                string srcName = reader.GetString(8);
                string srcType = reader.GetString(9);

                string tgtUid = reader.GetString(10);
                long tgtDoc = reader.IsDBNull(11) ? 0 : reader.GetInt64(11);
                string tgtName = reader.GetString(12);
                string tgtType = reader.GetString(13);

                if (!matchedNodes.ContainsKey(srcDbId))
                {
                    matchedNodes[srcDbId] = new EntityGraphNode(srcUid, srcDoc > 0 ? srcDoc.ToString() : "", chunkId ?? "", srcName, srcType, "");
                    nextHopNodeIds.Add(srcDbId);
                }

                if (!matchedNodes.ContainsKey(tgtDbId))
                {
                    matchedNodes[tgtDbId] = new EntityGraphNode(tgtUid, tgtDoc > 0 ? tgtDoc.ToString() : "", chunkId ?? "", tgtName, tgtType, "");
                    nextHopNodeIds.Add(tgtDbId);
                }

                matchedEdges.Add(new EntityGraphEdge(edgeUid, srcUid, tgtUid, relType, weight, chunkId ?? ""));
                if (!string.IsNullOrEmpty(chunkId)) relatedChunkIds.Add(chunkId);
            }

            currentHopNodeIds = nextHopNodeIds;
        }

        float score = Math.Min(1.0f, (matchedNodes.Count * 0.1f) + (matchedEdges.Count * 0.15f));
        return new GraphRetrievalResult(matchedNodes.Values.ToList(), matchedEdges, relatedChunkIds.ToList(), score);
    }

    public async Task<GraphRetrievalResult> GetFullGraphAsync(
        int limit = 200,
        string? documentId = null,
        string? entityType = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var nodesDict = new Dictionary<long, EntityGraphNode>();
        var nodeUidToDbId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<EntityGraphEdge>();
        var chunkIds = new HashSet<string>();

        // Query nodes
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            var clauses = new List<string>();
            if (!string.IsNullOrWhiteSpace(documentId) && long.TryParse(documentId, out long dId))
            {
                clauses.Add("document_id = $docId");
                cmd.AddParameter("$docId", dId);
            }
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                clauses.Add("type = $type");
                cmd.AddParameter("$type", entityType);
            }

            string whereClause = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
            cmd.CommandText = $"""
                SELECT id, node_uid, document_id, chunk_id, name, type, description
                FROM document_graph_nodes
                {whereClause}
                ORDER BY id DESC
                LIMIT $limit;
                """;
            cmd.AddParameter("$limit", limit);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long dbId = reader.GetInt64(0);
                string nodeUid = reader.GetString(1);
                long docIdVal = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                string? chunkId = reader.IsDBNull(3) ? null : reader.GetInt64(3).ToString();
                string name = reader.GetString(4);
                string type = reader.GetString(5);
                string? desc = reader.IsDBNull(6) ? null : reader.GetString(6);

                var node = new EntityGraphNode(nodeUid, docIdVal > 0 ? docIdVal.ToString() : "", chunkId ?? "", name, type, desc ?? "");
                nodesDict[dbId] = node;
                nodeUidToDbId[nodeUid] = dbId;
                if (!string.IsNullOrEmpty(chunkId)) chunkIds.Add(chunkId);
            }
        }

        if (nodesDict.Count == 0)
        {
            return new GraphRetrievalResult(Array.Empty<EntityGraphNode>(), Array.Empty<EntityGraphEdge>(), Array.Empty<string>(), 0f);
        }

        // Query connecting edges
        string inClause = string.Join(",", nodesDict.Keys);
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT e.edge_uid, sn.node_uid as src_uid, tn.node_uid as tgt_uid, e.relation_type, e.weight, e.chunk_id
                FROM document_graph_edges e
                JOIN document_graph_nodes sn ON e.source_node_id = sn.id
                JOIN document_graph_nodes tn ON e.target_node_id = tn.id
                WHERE e.source_node_id IN ({inClause}) AND e.target_node_id IN ({inClause})
                LIMIT $limit;
                """;
            cmd.AddParameter("$limit", limit * 2);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                string edgeUid = reader.GetString(0);
                string srcUid = reader.GetString(1);
                string tgtUid = reader.GetString(2);
                string relType = reader.GetString(3);
                float weight = (float)reader.GetDouble(4);
                string? chunkId = reader.IsDBNull(5) ? null : reader.GetInt64(5).ToString();

                edges.Add(new EntityGraphEdge(edgeUid, srcUid, tgtUid, relType, weight, chunkId ?? ""));
                if (!string.IsNullOrEmpty(chunkId)) chunkIds.Add(chunkId);
            }
        }

        return new GraphRetrievalResult(nodesDict.Values.ToList(), edges, chunkIds.ToList(), 1.0f);
    }
}

