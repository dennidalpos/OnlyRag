import React, { useMemo, useState, useRef } from "react";
import { EntityGraphNode, EntityGraphEdge } from "../../apiTypes/graph";

type KnowledgeGraphCanvasProps = {
  nodes: EntityGraphNode[];
  edges: EntityGraphEdge[];
  highlightedNodeIds?: string[];
  highlightedEdgeIds?: string[];
  onSelectNode?: (node: EntityGraphNode | null) => void;
  selectedNodeId?: string | null;
};

type RenderNode = EntityGraphNode & {
  x: number;
  y: number;
  color: string;
};

export const KnowledgeGraphCanvas: React.FC<KnowledgeGraphCanvasProps> = ({
  nodes,
  edges,
  highlightedNodeIds = [],
  highlightedEdgeIds = [],
  onSelectNode,
  selectedNodeId
}) => {
  const [zoom, setZoom] = useState<number>(1);
  const [pan, setPan] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const [isDragging, setIsDragging] = useState<boolean>(false);
  const [dragStart, setDragStart] = useState<{ x: number; y: number }>({ x: 0, y: 0 });
  const [hoveredNodeId, setHoveredNodeId] = useState<string | null>(null);

  const containerRef = useRef<HTMLDivElement>(null);

  // Compute node colors by entity type
  const getNodeColor = (type: string): string => {
    const t = type.toLowerCase();
    if (t.includes("concept") || t.includes("domain") || t.includes("topic")) return "#6366f1"; // Indigo
    if (t.includes("person") || t.includes("user") || t.includes("author")) return "#10b981"; // Emerald
    if (t.includes("location") || t.includes("org") || t.includes("place")) return "#f59e0b"; // Amber
    if (t.includes("code") || t.includes("class") || t.includes("method") || t.includes("symbol")) return "#a855f7"; // Purple
    if (t.includes("doc") || t.includes("file") || t.includes("article")) return "#ec4899"; // Rose
    return "#3b82f6"; // Blue
  };

  // Layout calculation: circular / force-spread layout
  const layoutNodes = useMemo<RenderNode[]>(() => {
    if (nodes.length === 0) return [];
    const width = 800;
    const height = 600;
    const centerX = width / 2;
    const centerY = height / 2;
    const radius = Math.min(width, height) * 0.35;

    return nodes.map((node, idx) => {
      const angle = (2 * Math.PI * idx) / nodes.length;
      // Add slight jitter for visual distinction if many nodes
      const distOffset = (idx % 3) * 25;
      const x = centerX + (radius + distOffset) * Math.cos(angle);
      const y = centerY + (radius + distOffset) * Math.sin(angle);
      return {
        ...node,
        x,
        y,
        color: getNodeColor(node.type)
      };
    });
  }, [nodes]);

  const nodeMap = useMemo(() => {
    const map = new Map<string, RenderNode>();
    layoutNodes.forEach((n) => map.set(n.nodeId, n));
    return map;
  }, [layoutNodes]);

  // Handle pan drag
  const handleMouseDown = (e: React.MouseEvent) => {
    if (e.target !== containerRef.current && (e.target as HTMLElement).tagName !== "svg") return;
    setIsDragging(true);
    setDragStart({ x: e.clientX - pan.x, y: e.clientY - pan.y });
  };

  const handleMouseMove = (e: React.MouseEvent) => {
    if (!isDragging) return;
    setPan({ x: e.clientX - dragStart.x, y: e.clientY - dragStart.y });
  };

  const handleMouseUp = () => {
    setIsDragging(false);
  };

  const handleWheel = (e: React.WheelEvent) => {
    e.preventDefault();
    const delta = e.deltaY < 0 ? 1.1 : 0.9;
    setZoom((prev) => Math.min(Math.max(prev * delta, 0.4), 3.0));
  };

  const resetView = () => {
    setZoom(1);
    setPan({ x: 0, y: 0 });
  };

  // Highlighted set lookups
  const highlightNodeSet = useMemo(() => new Set(highlightedNodeIds), [highlightedNodeIds]);
  const highlightEdgeSet = useMemo(() => new Set(highlightedEdgeIds), [highlightedEdgeIds]);

  return (
    <div
      ref={containerRef}
      className="graph-canvas-container"
      onMouseDown={handleMouseDown}
      onMouseMove={handleMouseMove}
      onMouseUp={handleMouseUp}
      onWheel={handleWheel}
      style={{
        position: "relative",
        width: "100%",
        height: "550px",
        overflow: "hidden",
        backgroundColor: "var(--color-bg-secondary, #0f172a)",
        borderRadius: "12px",
        border: "1px solid var(--color-border, #1e293b)",
        cursor: isDragging ? "grabbing" : "grab",
        userSelect: "none"
      }}
    >
      {/* Visual Controls Overlay */}
      <div
        style={{
          position: "absolute",
          top: "16px",
          right: "16px",
          display: "flex",
          gap: "8px",
          zIndex: 10
        }}
      >
        <button
          className="btn-icon"
          onClick={() => setZoom((z) => Math.min(z * 1.2, 3))}
          title="Ingrandisci (Zoom In)"
          style={btnStyle}
        >
          +
        </button>
        <button
          className="btn-icon"
          onClick={() => setZoom((z) => Math.max(z * 0.8, 0.4))}
          title="Riduci (Zoom Out)"
          style={btnStyle}
        >
          -
        </button>
        <button
          className="btn-icon"
          onClick={resetView}
          title="Reimposta vista"
          style={btnStyle}
        >
          ↺
        </button>
      </div>

      {/* Main SVG Render Area */}
      <svg
        width="100%"
        height="100%"
        viewBox="0 0 800 600"
        style={{ display: "block" }}
      >
        <g transform={`translate(${pan.x}, ${pan.y}) scale(${zoom})`}>
          {/* Render Edges */}
          {edges.map((edge) => {
            const src = nodeMap.get(edge.sourceNodeId);
            const tgt = nodeMap.get(edge.targetNodeId);
            if (!src || !tgt) return null;

            const isHighlighted = highlightEdgeSet.has(edge.edgeId) ||
              (hoveredNodeId && (src.nodeId === hoveredNodeId || tgt.nodeId === hoveredNodeId));

            const strokeColor = isHighlighted ? "#fbbf24" : "var(--color-border-subtle, #334155)";
            const strokeWidth = isHighlighted ? Math.max(edge.weight * 2.5, 3) : Math.max(edge.weight * 1.2, 1);

            const midX = (src.x + tgt.x) / 2;
            const midY = (src.y + tgt.y) / 2;

            return (
              <g key={edge.edgeId}>
                <line
                  x1={src.x}
                  y1={src.y}
                  x2={tgt.x}
                  y2={tgt.y}
                  stroke={strokeColor}
                  strokeWidth={strokeWidth}
                  strokeDasharray={isHighlighted ? "4 2" : undefined}
                  opacity={isHighlighted ? 1 : 0.65}
                />
                {edge.relationType && (
                  <text
                    x={midX}
                    y={midY - 4}
                    fill="var(--color-text-muted, #94a3b8)"
                    fontSize="10"
                    textAnchor="middle"
                    pointerEvents="none"
                    style={{ background: "#090d16", padding: "2px" }}
                  >
                    {edge.relationType}
                  </text>
                )}
              </g>
            );
          })}

          {/* Render Nodes */}
          {layoutNodes.map((node) => {
            const isSelected = selectedNodeId === node.nodeId;
            const isHovered = hoveredNodeId === node.nodeId;
            const isHighlighted = highlightNodeSet.has(node.nodeId);
            const radius = isSelected || isHovered ? 22 : 18;

            return (
              <g
                key={node.nodeId}
                transform={`translate(${node.x}, ${node.y})`}
                onClick={(e) => {
                  e.stopPropagation();
                  onSelectNode?.(node);
                }}
                onMouseEnter={() => setHoveredNodeId(node.nodeId)}
                onMouseLeave={() => setHoveredNodeId(null)}
                style={{ cursor: "pointer" }}
              >
                {/* Outer Glow Halo */}
                {(isSelected || isHighlighted || isHovered) && (
                  <circle
                    r={radius + 6}
                    fill={node.color}
                    opacity={isSelected ? 0.45 : 0.25}
                  />
                )}

                {/* Node Circle */}
                <circle
                  r={radius}
                  fill={node.color}
                  stroke={isSelected ? "#ffffff" : isHighlighted ? "#fbbf24" : "rgba(255,255,255,0.3)"}
                  strokeWidth={isSelected ? 3 : isHighlighted ? 2.5 : 1.5}
                />

                {/* Node Label */}
                <text
                  y={radius + 14}
                  fill={isSelected ? "#ffffff" : "var(--color-text-main, #f8fafc)"}
                  fontSize="11"
                  fontWeight={isSelected || isHovered ? "600" : "400"}
                  textAnchor="middle"
                  pointerEvents="none"
                >
                  {node.name.length > 16 ? `${node.name.substring(0, 14)}...` : node.name}
                </text>
              </g>
            );
          })}
        </g>
      </svg>
    </div>
  );
};

const btnStyle: React.CSSProperties = {
  backgroundColor: "rgba(30, 41, 59, 0.85)",
  color: "#f8fafc",
  border: "1px solid rgba(255, 255, 255, 0.15)",
  borderRadius: "6px",
  width: "32px",
  height: "32px",
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  cursor: "pointer",
  fontSize: "14px",
  fontWeight: "bold",
  backdropFilter: "blur(8px)"
};
