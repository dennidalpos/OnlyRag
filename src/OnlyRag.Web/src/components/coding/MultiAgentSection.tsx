import { useEffect, useState } from "react";
import {
  MultiAgentOrchestrationStatus,
  getMultiAgentOrchestrationStatus,
  startMultiAgentOrchestration
} from "../../apiClient";
import { Network, Play, CheckCircle2, Clock, MessageSquare } from "lucide-react";

export function MultiAgentSection() {
  const [goal, setGoal] = useState("");
  const [status, setStatus] = useState<MultiAgentOrchestrationStatus | null>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleStart() {
    if (!goal.trim()) return;
    try {
      setIsStarting(true);
      setError(null);
      const res = await startMultiAgentOrchestration(goal.trim());
      setStatus(res);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsStarting(false);
    }
  }

  useEffect(() => {
    if (!status || status.isCompleted || status.hasFailed) return;

    const interval = setInterval(async () => {
      try {
        const updated = await getMultiAgentOrchestrationStatus(status.orchestrationId);
        setStatus(updated);
      } catch (err) {
        console.warn("Errore aggiornamento orchestrazione:", err);
      }
    }, 1500);

    return () => clearInterval(interval);
  }, [status]);

  return (
    <div className="card card--spaced" style={{ padding: "20px" }}>
      <h3 style={{ margin: "0 0 16px 0", display: "flex", alignItems: "center", gap: "8px" }}>
        <Network className="icon" style={{ color: "#8b5cf6" }} /> Orchestrazione Avanzata Multi-Agenti
      </h3>

      <div style={{ marginBottom: "20px" }}>
        <label style={{ display: "block", fontWeight: 600, marginBottom: "8px" }}>
          Obiettivo Complesso o Task da Scomporre tra Agenti Specializzati:
        </label>
        <div style={{ display: "flex", gap: "10px" }}>
          <input
            className="input"
            type="text"
            placeholder="Es: Analisi architetturale, ricerca documentazione e generazione modulo di pagamento RAG"
            value={goal}
            onChange={(e) => setGoal(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") void handleStart();
            }}
            style={{ flex: 1 }}
          />
          <button disabled={isStarting || !goal.trim()} className="button button--primary" onClick={() => void handleStart()}>
            <Play className="icon icon--sm" /> {isStarting ? "Avvio Agenti..." : "Avvia Flusso Multi-Agente"}
          </button>
        </div>
      </div>

      {error && <div className="feedback-banner feedback-banner--error">{error}</div>}

      {status && (
        <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px", background: "#f8fafc", padding: "12px", borderRadius: "8px" }}>
            <div>
              <strong>ID Orchestrazione:</strong> <code>{status.orchestrationId}</code>
            </div>
            <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
              <strong>Stato:</strong>{" "}
              {status.isCompleted ? (
                <span style={{ color: "#10b981", fontWeight: 600, display: "flex", alignItems: "center", gap: "4px" }}>
                  <CheckCircle2 size={16} /> Completato
                </span>
              ) : (
                <span style={{ color: "#3b82f6", fontWeight: 600, display: "flex", alignItems: "center", gap: "4px" }}>
                  <Clock size={16} /> In Esecuzione...
                </span>
              )}
            </div>
          </div>

          <h4 style={{ margin: "16px 0 12px 0" }}>Grafo dei Sub-Task ed Esecuzione Agenti Paralleli:</h4>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "14px", marginBottom: "24px" }}>
            {status.subtasks.map((task, idx) => (
              <div
                key={task.subtaskId}
                style={{
                  border: `2px solid ${
                    task.status === "Completed"
                      ? "#10b981"
                      : task.status === "Running"
                      ? "#3b82f6"
                      : task.status === "Failed"
                      ? "#ef4444"
                      : "#cbd5e1"
                  }`,
                  borderRadius: "8px",
                  padding: "14px",
                  background: task.status === "Running" ? "#eff6ff" : "#ffffff",
                  transition: "all 0.3s ease"
                }}
              >
                <div style={{ fontSize: "0.75rem", color: "#64748b", fontWeight: 700, textTransform: "uppercase" }}>
                  Fase {idx + 1} • {task.role}
                </div>
                <div style={{ fontWeight: 600, fontSize: "0.95rem", margin: "6px 0" }}>{task.goal}</div>
                <div style={{ fontSize: "0.8rem", color: "#475569" }}>
                  Stato: <strong>{task.status}</strong>
                </div>
                {task.output && <div style={{ fontSize: "0.75rem", marginTop: "8px", color: "#10b981" }}>✓ {task.output}</div>}
              </div>
            ))}
          </div>

          <h4 style={{ margin: "16px 0 12px 0", display: "flex", alignItems: "center", gap: "6px" }}>
            <MessageSquare size={18} /> Bus Messaggi Inter-Agente in Tempo Reale:
          </h4>
          <div style={{ background: "#0f172a", color: "#e2e8f0", padding: "14px", borderRadius: "8px", fontFamily: "monospace", fontSize: "0.85rem", maxHeight: "200px", overflowY: "auto" }}>
            {status.messages.map((msg) => (
              <div key={msg.messageId} style={{ marginBottom: "6px" }}>
                <span style={{ color: "#94a3b8" }}>[{new Date(msg.sentAtUtc).toLocaleTimeString()}]</span>{" "}
                <span style={{ color: "#818cf8", fontWeight: 700 }}>{msg.senderRole}</span> →{" "}
                <span style={{ color: "#38bdf8", fontWeight: 700 }}>{msg.recipientRole}</span>: {msg.messageText}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
