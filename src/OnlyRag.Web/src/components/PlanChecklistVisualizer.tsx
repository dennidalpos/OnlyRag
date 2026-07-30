import React from "react";

type PlanChecklistVisualizerProps = {
  planMarkdown: string;
};

export const PlanChecklistVisualizer: React.FC<PlanChecklistVisualizerProps> = ({ planMarkdown }) => {
  if (!planMarkdown) return null;

  const lines = planMarkdown.split("\n").map(l => l.trim()).filter(Boolean);

  return (
    <div className="plan-checklist-card" style={{
      background: "#0f172a",
      border: "1px solid #3b82f6",
      borderRadius: "8px",
      padding: "12px 16px",
      margin: "10px 0",
      boxShadow: "0 0 16px rgba(59, 130, 246, 0.15)"
    }}>
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#60a5fa" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <polyline points="9 11 12 14 22 4" />
          <path d="M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11" />
        </svg>
        <span style={{ fontSize: "0.85rem", fontWeight: 700, color: "#60a5fa", textTransform: "uppercase", letterSpacing: "0.05em" }}>
          Piano d&apos;Azione SOTA (Checklist Visiva)
        </span>
      </div>

      <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
        {lines.map((line, idx) => {
          const isCompleted = line.startsWith("[x]") || line.startsWith("- [x]");
          const isInProgress = line.startsWith("[>]") || line.startsWith("- [>]");
          const isFailed = line.startsWith("[!]") || line.startsWith("- [!]");
          const cleanText = line.replace(/^(?:-\s*)?\[[ x>!]\]\s*/, "");

          return (
            <div key={idx} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: "0.86rem" }}>
              {isCompleted && <span style={{ color: "#10b981", fontWeight: "bold" }}>✓</span>}
              {isInProgress && <span style={{ color: "#3b82f6", fontWeight: "bold" }}>⏳</span>}
              {isFailed && <span style={{ color: "#ef4444", fontWeight: "bold" }}>✕</span>}
              {!isCompleted && !isInProgress && !isFailed && <span style={{ color: "#64748b" }}>○</span>}

              <span style={{
                color: isCompleted ? "#94a3b8" : isInProgress ? "#f8fafc" : "#cbd5e1",
                textDecoration: isCompleted ? "line-through" : "none",
                fontWeight: isInProgress ? 600 : 400
              }}>
                {cleanText}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
};
