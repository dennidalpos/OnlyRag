import { CheckCircle2, Clock, MessageSquare, Network, AlertCircle } from "lucide-react";
import type { MultiAgentOrchestrationStatus } from "../../apiClient";

type MultiAgentOrchestrationCardProps = {
  status: MultiAgentOrchestrationStatus;
};

export function MultiAgentOrchestrationCard({ status }: MultiAgentOrchestrationCardProps) {
  return (
    <div
      className="multi-agent-orchestration-card"
      style={{
        margin: "12px 0",
        padding: "16px",
        borderRadius: "10px",
        background: "rgba(15, 23, 42, 0.6)",
        border: "1px solid rgba(139, 92, 246, 0.3)",
        boxShadow: "0 4px 12px rgba(0, 0, 0, 0.15)"
      }}
    >
      {/* HEADER */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "14px",
          paddingBottom: "10px",
          borderBottom: "1px solid rgba(255, 255, 255, 0.08)"
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
          <Network className="icon" style={{ color: "#a855f7" }} size={20} />
          <span style={{ fontWeight: 700, fontSize: "0.95rem", color: "#e2e8f0" }}>
            Orchestrazione Avanzata Multi-Agenti
          </span>
          <code
            style={{
              fontSize: "0.75rem",
              background: "rgba(255, 255, 255, 0.06)",
              padding: "2px 6px",
              borderRadius: "4px",
              color: "#94a3b8"
            }}
          >
            {status.orchestrationId}
          </code>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem" }}>
          {status.isCompleted ? (
            <span style={{ color: "#4ade80", fontWeight: 600, display: "flex", alignItems: "center", gap: "4px" }}>
              <CheckCircle2 size={16} /> Completato
            </span>
          ) : status.hasFailed ? (
            <span style={{ color: "#f87171", fontWeight: 600, display: "flex", alignItems: "center", gap: "4px" }}>
              <AlertCircle size={16} /> Fallito
            </span>
          ) : (
            <span style={{ color: "#38bdf8", fontWeight: 600, display: "flex", alignItems: "center", gap: "4px" }}>
              <Clock size={16} className="animate-spin" /> In Esecuzione...
            </span>
          )}
        </div>
      </div>

      {/* SUBTASK GRAPH */}
      {status.subtasks && status.subtasks.length > 0 && (
        <div style={{ marginBottom: "16px" }}>
          <div style={{ fontSize: "0.8rem", color: "#94a3b8", fontWeight: 600, marginBottom: "10px" }}>
            Grafo dei Sub-Task ed Agenti Paralleli:
          </div>
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
              gap: "10px"
            }}
          >
            {status.subtasks.map((task, idx) => {
              const isDone = task.status === "Completed";
              const isRunning = task.status === "Running";
              const isFailed = task.status === "Failed";

              const borderColor = isDone
                ? "#22c55e"
                : isRunning
                ? "#3b82f6"
                : isFailed
                ? "#ef4444"
                : "rgba(255, 255, 255, 0.1)";

              const bgColor = isRunning
                ? "rgba(59, 130, 246, 0.12)"
                : isDone
                ? "rgba(34, 197, 94, 0.08)"
                : "rgba(255, 255, 255, 0.03)";

              return (
                <div
                  key={task.subtaskId}
                  style={{
                    border: `1.5px solid ${borderColor}`,
                    borderRadius: "8px",
                    padding: "10px 12px",
                    background: bgColor,
                    transition: "all 0.25s ease"
                  }}
                >
                  <div
                    style={{
                      fontSize: "0.7rem",
                      color: "#94a3b8",
                      fontWeight: 700,
                      textTransform: "uppercase",
                      letterSpacing: "0.5px"
                    }}
                  >
                    Fase {idx + 1} • {task.role}
                  </div>
                  <div
                    style={{
                      fontWeight: 600,
                      fontSize: "0.85rem",
                      margin: "4px 0",
                      color: "#f1f5f9"
                    }}
                  >
                    {task.goal}
                  </div>
                  <div style={{ fontSize: "0.75rem", color: "#cbd5e1" }}>
                    Stato: <strong style={{ color: borderColor }}>{task.status}</strong>
                  </div>
                  {task.output && (
                    <div
                      style={{
                        fontSize: "0.72rem",
                        marginTop: "6px",
                        color: "#4ade80",
                        whiteSpace: "nowrap",
                        overflow: "hidden",
                        textOverflow: "ellipsis"
                      }}
                    >
                      ✓ {task.output}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>
      )}

      {/* REAL-TIME INTER-AGENT MESSAGES */}
      {status.messages && status.messages.length > 0 && (
        <div>
          <div
            style={{
              fontSize: "0.8rem",
              color: "#94a3b8",
              fontWeight: 600,
              marginBottom: "8px",
              display: "flex",
              alignItems: "center",
              gap: "6px"
            }}
          >
            <MessageSquare size={14} /> Bus Messaggi Inter-Agente in Tempo Reale:
          </div>
          <div
            style={{
              background: "#090d16",
              color: "#e2e8f0",
              padding: "10px 12px",
              borderRadius: "6px",
              fontFamily: "monospace",
              fontSize: "0.78rem",
              maxHeight: "150px",
              overflowY: "auto",
              border: "1px solid rgba(255, 255, 255, 0.05)"
            }}
          >
            {status.messages.map((msg) => (
              <div key={msg.messageId} style={{ marginBottom: "4px" }}>
                <span style={{ color: "#64748b" }}>
                  [{new Date(msg.sentAtUtc).toLocaleTimeString()}]
                </span>{" "}
                <span style={{ color: "#a78bfa", fontWeight: 700 }}>{msg.senderRole}</span> →{" "}
                <span style={{ color: "#38bdf8", fontWeight: 700 }}>{msg.recipientRole}</span>:{" "}
                <span style={{ color: "#cbd5e1" }}>{msg.messageText}</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
