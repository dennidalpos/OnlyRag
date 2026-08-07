import React, { useState, useEffect } from "react";
import { getGraphData, searchGraph } from "../../apiClient";
import { EntityGraphNode, GraphRetrievalResult } from "../../apiTypes/graph";
import { KnowledgeGraphCanvas } from "./KnowledgeGraphCanvas";

export const KnowledgeGraphSection: React.FC = () => {
  const [graphResult, setGraphResult] = useState<GraphRetrievalResult | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [selectedEntityType, setSelectedEntityType] = useState<string>("");
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [isSearching, setIsSearching] = useState<boolean>(false);

  // Selection and Highlight
  const [selectedNode, setSelectedNode] = useState<EntityGraphNode | null>(null);
  const [highlightedNodeIds, setHighlightedNodeIds] = useState<string[]>([]);
  const [highlightedEdgeIds, setHighlightedEdgeIds] = useState<string[]>([]);

  const loadGraph = async () => {
    try {
      setLoading(true);
      setError(null);
      const res = await getGraphData(200, undefined, selectedEntityType || undefined);
      setGraphResult(res);
    } catch (err) {
      setError((err as Error).message || "Impossibile caricare il grafo di conoscenza.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadGraph();
  }, [selectedEntityType]);

  const handleExecuteSearch = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!searchQuery.trim()) {
      setHighlightedNodeIds([]);
      setHighlightedEdgeIds([]);
      return;
    }

    try {
      setIsSearching(true);
      const res = await searchGraph({ query: searchQuery.trim(), maxHops: 2, maxNodes: 30 });
      setHighlightedNodeIds(res.nodes.map((n) => n.nodeId));
      setHighlightedEdgeIds(res.edges.map((e) => e.edgeId));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsSearching(false);
    }
  };

  const entityTypes = ["Concept", "Person", "Location", "Code", "Document", "Organization"];

  return (
    <div className="section-container" style={{ padding: "24px", color: "var(--color-text-main, #f8fafc)" }}>
      {/* Section Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "20px" }}>
        <div>
          <h2 style={{ fontSize: "22px", fontWeight: "700", margin: 0, color: "var(--color-primary-light, #818cf8)" }}>
            Visualizzatore Grafo di Conoscenza (GraphRAG Canvas)
          </h2>
          <p style={{ margin: "4px 0 0", fontSize: "14px", color: "var(--color-text-muted, #94a3b8)" }}>
            Esplora le relazioni semantiche tra entità documentali, concetti e simboli estratte da GraphRetrievalService.
          </p>
        </div>
        <button
          onClick={loadGraph}
          disabled={loading}
          style={{
            padding: "8px 16px",
            borderRadius: "8px",
            backgroundColor: "var(--color-primary, #4f46e5)",
            color: "#fff",
            border: "none",
            cursor: "pointer",
            fontWeight: "600"
          }}
        >
          {loading ? "Ricaricamento..." : "Aggiorna Grafo"}
        </button>
      </div>

      {/* Control & Search Bar */}
      <div
        style={{
          display: "flex",
          gap: "16px",
          marginBottom: "16px",
          alignItems: "center",
          flexWrap: "wrap",
          background: "var(--color-bg-tertiary, #1e293b)",
          padding: "12px 16px",
          borderRadius: "10px"
        }}
      >
        {/* Entity Type Filter */}
        <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
          <label style={{ fontSize: "13px", fontWeight: "500" }}>Tipo Entità:</label>
          <select
            value={selectedEntityType}
            onChange={(e) => setSelectedEntityType(e.target.value)}
            style={{
              padding: "6px 12px",
              borderRadius: "6px",
              backgroundColor: "var(--color-bg-secondary, #0f172a)",
              color: "#fff",
              border: "1px solid var(--color-border, #334155)",
              fontSize: "13px"
            }}
          >
            <option value="">Tutti i tipi</option>
            {entityTypes.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </div>

        {/* Traversal Search Form */}
        <form onSubmit={handleExecuteSearch} style={{ display: "flex", gap: "8px", flex: 1, minWidth: "300px" }}>
          <input
            type="text"
            placeholder="Cerca cammino di traversamento RAG (es. C# async memory)..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            style={{
              flex: 1,
              padding: "6px 12px",
              borderRadius: "6px",
              backgroundColor: "var(--color-bg-secondary, #0f172a)",
              color: "#fff",
              border: "1px solid var(--color-border, #334155)",
              fontSize: "13px"
            }}
          />
          <button
            type="submit"
            disabled={isSearching}
            style={{
              padding: "6px 14px",
              borderRadius: "6px",
              backgroundColor: "#2563eb",
              color: "#fff",
              border: "none",
              cursor: "pointer",
              fontSize: "13px",
              fontWeight: "500"
            }}
          >
            {isSearching ? "Ricerca..." : "Evidenzia Cammino"}
          </button>
        </form>
      </div>

      {/* Status / Error Message */}
      {error && (
        <div
          style={{
            padding: "12px",
            marginBottom: "16px",
            backgroundColor: "rgba(239, 68, 68, 0.15)",
            border: "1px solid #ef4444",
            borderRadius: "8px",
            color: "#fca5a5",
            fontSize: "13px"
          }}
        >
          {error}
        </div>
      )}

      {/* Main Graph & Inspector Grid */}
      <div style={{ display: "grid", gridTemplateColumns: selectedNode ? "1fr 320px" : "1fr", gap: "16px" }}>
        <KnowledgeGraphCanvas
          nodes={graphResult?.nodes || []}
          edges={graphResult?.edges || []}
          highlightedNodeIds={highlightedNodeIds}
          highlightedEdgeIds={highlightedEdgeIds}
          onSelectNode={(node) => setSelectedNode(node)}
          selectedNodeId={selectedNode?.nodeId}
        />

        {/* Selected Node Details Drawer */}
        {selectedNode && (
          <div
            style={{
              backgroundColor: "var(--color-bg-secondary, #0f172a)",
              borderRadius: "12px",
              border: "1px solid var(--color-border, #1e293b)",
              padding: "16px",
              display: "flex",
              flexDirection: "column",
              gap: "12px"
            }}
          >
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
              <h3 style={{ margin: 0, fontSize: "16px", fontWeight: "600" }}>Ispezione Dettaglio Entità</h3>
              <button
                onClick={() => setSelectedNode(null)}
                style={{ background: "none", border: "none", color: "#94a3b8", cursor: "pointer", fontSize: "16px" }}
              >
                ✕
              </button>
            </div>

            <div>
              <span
                style={{
                  display: "inline-block",
                  padding: "2px 8px",
                  borderRadius: "4px",
                  fontSize: "11px",
                  fontWeight: "600",
                  backgroundColor: "rgba(99, 102, 241, 0.2)",
                  color: "#818cf8",
                  marginBottom: "8px"
                }}
              >
                {selectedNode.type}
              </span>
              <h4 style={{ margin: 0, fontSize: "18px", color: "#f8fafc" }}>{selectedNode.name}</h4>
            </div>

            {selectedNode.description && (
              <div>
                <label style={{ fontSize: "12px", color: "#94a3b8", display: "block" }}>Descrizione:</label>
                <p style={{ margin: "4px 0 0", fontSize: "13px", lineHeight: "1.4" }}>{selectedNode.description}</p>
              </div>
            )}

            <div style={{ borderTop: "1px solid #1e293b", paddingTop: "10px", fontSize: "12px", color: "#94a3b8" }}>
              <div><strong>Node ID:</strong> {selectedNode.nodeId}</div>
              {selectedNode.documentId && <div><strong>Document ID:</strong> {selectedNode.documentId}</div>}
              {selectedNode.chunkId && <div><strong>Chunk ID:</strong> {selectedNode.chunkId}</div>}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
