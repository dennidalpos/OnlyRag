import React, { useState } from "react";

type UnifiedDiffViewerProps = {
  patch: string;
  filename?: string;
  title?: string;
};

export const UnifiedDiffViewer: React.FC<UnifiedDiffViewerProps> = ({ patch, filename, title = "DIFF PATCH" }) => {
  const [collapsed, setCollapsed] = useState(false);

  if (!patch || !patch.trim()) return null;

  const lines = patch.split("\n");
  const additions = lines.filter((l) => l.startsWith("+") && !l.startsWith("+++")).length;
  const deletions = lines.filter((l) => l.startsWith("-") && !l.startsWith("---")).length;

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
        <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <span style={{ color: "#38bdf8", fontWeight: 700 }}>{title}</span>
          {filename && <span style={{ color: "#f8fafc", fontWeight: 600 }}>({filename})</span>}
          <div style={{ display: "flex", gap: 6, fontSize: "0.75rem", fontWeight: 600 }}>
            {additions > 0 && <span style={{ color: "#34d399", background: "rgba(16,185,129,0.2)", padding: "1px 6px", borderRadius: 4 }}>+{additions}</span>}
            {deletions > 0 && <span style={{ color: "#f87171", background: "rgba(239,68,68,0.2)", padding: "1px 6px", borderRadius: 4 }}>-{deletions}</span>}
          </div>
        </div>
        <span style={{ fontSize: "0.75rem" }}>{collapsed ? "Espandi ▼" : "Comprimi ▲"}</span>
      </div>

      {!collapsed && (
        <div style={{ overflowX: "auto", padding: "8px 0", maxHeight: "350px" }}>
          {lines.map((line, idx) => {
            let bg = "transparent";
            let color = "#cbd5e1";
            const isAdd = line.startsWith("+") && !line.startsWith("+++");
            const isDel = line.startsWith("-") && !line.startsWith("---");
            const isHeader = line.startsWith("---") || line.startsWith("+++") || line.startsWith("@@");

            if (isAdd) {
              bg = "rgba(16, 185, 129, 0.15)";
              color = "#34d399";
            } else if (isDel) {
              bg = "rgba(239, 68, 68, 0.15)";
              color = "#f87171";
            } else if (isHeader) {
              color = "#38bdf8";
              bg = "rgba(56, 189, 248, 0.08)";
            }

            return (
              <div key={idx} style={{
                background: bg,
                color: color,
                padding: "2px 12px",
                whiteSpace: "pre",
                lineHeight: "1.4",
                display: "flex",
                gap: "12px"
              }}>
                <span style={{
                  userSelect: "none",
                  color: "#475569",
                  width: "28px",
                  textAlign: "right",
                  flexShrink: 0,
                  fontSize: "0.76rem"
                }}>
                  {idx + 1}
                </span>
                <span style={{ flex: 1, minWidth: 0 }}>{line}</span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

