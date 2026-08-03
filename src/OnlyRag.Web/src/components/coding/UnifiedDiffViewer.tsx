import React, { useState } from "react";

type UnifiedDiffViewerProps = {
  patch: string;
  filename?: string;
};

export const UnifiedDiffViewer: React.FC<UnifiedDiffViewerProps> = ({ patch, filename }) => {
  const [collapsed, setCollapsed] = useState(false);

  if (!patch || !patch.trim()) return null;

  const lines = patch.split("\n");

  return (
    <div className="unified-diff-viewer" style={{
      margin: "8px 0",
      background: "#090d16",
      border: "1px solid #1e293b",
      borderRadius: "6px",
      overflow: "hidden",
      fontFamily: "ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace",
      fontSize: "0.82rem"
    }}>
      <div style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: "6px 12px",
        background: "#0f172a",
        borderBottom: "1px solid #1e293b",
        color: "#94a3b8",
        cursor: "pointer"
      }} onClick={() => setCollapsed(!collapsed)}>
        <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
          <span style={{ color: "#38bdf8", fontWeight: 700 }}>DIFF PATCH</span>
          {filename && <span style={{ color: "#f8fafc" }}>({filename})</span>}
        </div>
        <span style={{ fontSize: "0.75rem" }}>{collapsed ? "Espandi ▼" : "Comprimi ▲"}</span>
      </div>

      {!collapsed && (
        <div style={{ overflowX: "auto", padding: "8px 0", maxHeight: "350px" }}>
          {lines.map((line, idx) => {
            let bg = "transparent";
            let color = "#cbd5e1";
            if (line.startsWith("+")) {
              bg = "rgba(16, 185, 129, 0.15)";
              color = "#34d399";
            } else if (line.startsWith("-")) {
              bg = "rgba(239, 68, 68, 0.15)";
              color = "#f87171";
            } else if (line.startsWith("---") || line.startsWith("+++")) {
              color = "#38bdf8";
            }

            return (
              <div key={idx} style={{
                background: bg,
                color: color,
                padding: "1px 12px",
                whiteSpace: "pre",
                lineHeight: "1.4"
              }}>
                {line}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
