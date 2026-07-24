import { useEffect, useState } from "react";
import { apiRequest } from "../api";
import type { BackgroundTaskInfo, ManageTaskRequest } from "../api";

type BackgroundTaskDrawerProps = {
  isOpen: boolean;
  onClose: () => void;
};

export function BackgroundTaskDrawer({ isOpen, onClose }: BackgroundTaskDrawerProps) {
  const [tasks, setTasks] = useState<BackgroundTaskInfo[]>([]);
  const [expandedTaskId, setExpandedTaskId] = useState<string | null>(null);
  const [taskLogs, setTaskLogs] = useState<Record<string, string>>({});
  const [sendInputValue, setSendInputValue] = useState("");

  useEffect(() => {
    if (!isOpen) return;
    void fetchTasks();
    const interval = setInterval(() => void fetchTasks(), 5000);
    return () => clearInterval(interval);
  }, [isOpen]);

  async function fetchTasks() {
    try {
      const list = await apiRequest<BackgroundTaskInfo[]>("/api/agent/tasks");
      setTasks(list);
    } catch {
      // silenzioso
    }
  }

  async function fetchTaskLogs(taskId: string) {
    try {
      const res = await apiRequest<{ info: BackgroundTaskInfo; logs: string }>("/api/agent/tasks/manage", {
        method: "POST",
        body: JSON.stringify({ action: "status", taskId } as ManageTaskRequest)
      });
      setTaskLogs((prev) => ({ ...prev, [taskId]: res.logs }));
    } catch {
      // silenzioso
    }
  }

  async function handleKillTask(taskId: string) {
    try {
      await apiRequest<{ success: boolean }>("/api/agent/tasks/manage", {
        method: "POST",
        body: JSON.stringify({ action: "kill", taskId } as ManageTaskRequest)
      });
      void fetchTasks();
    } catch {
      // silenzioso
    }
  }

  async function handleSendInput(taskId: string) {
    if (!sendInputValue.trim()) return;
    try {
      await apiRequest<{ success: boolean }>("/api/agent/tasks/manage", {
        method: "POST",
        body: JSON.stringify({ action: "send_input", taskId, input: sendInputValue } as ManageTaskRequest)
      });
      setSendInputValue("");
    } catch {
      // silenzioso
    }
  }

  function handleToggleExpand(taskId: string) {
    if (expandedTaskId === taskId) {
      setExpandedTaskId(null);
    } else {
      setExpandedTaskId(taskId);
      void fetchTaskLogs(taskId);
    }
  }

  if (!isOpen) return null;

  return (
    <div
      className="modal-backdrop"
      onClick={onClose}
    >
      <div
        className="modal-content animate-fade-in"
        onClick={(e) => e.stopPropagation()}
        style={{ maxWidth: 700, maxHeight: "80vh", display: "flex", flexDirection: "column" }}
      >
        <div className="modal-header" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
          <h3 style={{ margin: 0, display: "flex", alignItems: "center", gap: 8 }}>
            <span>💻</span> Processi in Background ({tasks.length})
          </h3>
          <button type="button" className="button-secondary" onClick={onClose}>✕</button>
        </div>

        <div className="modal-body" style={{ flex: 1, overflowY: "auto", display: "flex", flexDirection: "column", gap: 10 }}>
          {tasks.length === 0 ? (
            <div style={{ textAlign: "center", color: "#64748b", padding: 32 }}>
              <div style={{ fontSize: "2rem", marginBottom: 8 }}>📋</div>
              <p>Nessun processo in background attivo.</p>
            </div>
          ) : (
            tasks.map((task) => (
              <div
                key={task.taskId}
                style={{
                  background: "#111827",
                  borderRadius: 8,
                  border: task.isRunning ? "1px solid #3b82f6" : "1px solid #334155",
                  overflow: "hidden"
                }}
              >
                {/* Task header */}
                <div
                  style={{
                    display: "flex", alignItems: "center", justifyContent: "space-between",
                    padding: "10px 14px", cursor: "pointer"
                  }}
                  onClick={() => handleToggleExpand(task.taskId)}
                >
                  <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                    <span style={{ fontSize: "0.9rem" }}>
                      {task.isRunning ? "🟢" : (task.exitCode === 0 ? "✅" : "❌")}
                    </span>
                    <div>
                      <div style={{ fontFamily: "monospace", fontSize: "0.85rem", color: "#e2e8f0", fontWeight: 600 }}>
                        {task.command.length > 60 ? task.command.slice(0, 60) + "…" : task.command}
                      </div>
                      <div style={{ fontSize: "0.75rem", color: "#94a3b8", marginTop: 2 }}>
                        ID: {task.taskId} · Avviato: {new Date(task.startedAt).toLocaleTimeString()}
                        {!task.isRunning && task.exitCode !== null && ` · Exit: ${task.exitCode}`}
                      </div>
                    </div>
                  </div>
                  <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
                    {task.isRunning && (
                      <button
                        type="button"
                        className="button button--small"
                        style={{ background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5", fontSize: "0.75rem", padding: "3px 8px", borderRadius: 6 }}
                        onClick={(e) => { e.stopPropagation(); void handleKillTask(task.taskId); }}
                      >
                        ⬛ Kill
                      </button>
                    )}
                    <span style={{ color: "#64748b", fontSize: "0.8rem" }}>
                      {expandedTaskId === task.taskId ? "▲" : "▼"}
                    </span>
                  </div>
                </div>

                {/* Expanded logs */}
                {expandedTaskId === task.taskId && (
                  <div style={{ borderTop: "1px solid #1e293b" }}>
                    <pre style={{
                      margin: 0, padding: "10px 14px", background: "#090d16",
                      fontSize: "0.78rem", color: "#cbd5e1", maxHeight: 250,
                      overflowY: "auto", whiteSpace: "pre-wrap", fontFamily: "monospace"
                    }}>
                      {taskLogs[task.taskId] || "Caricamento log..."}
                    </pre>

                    {task.isRunning && (
                      <div style={{ display: "flex", gap: 6, padding: "8px 14px", borderTop: "1px solid #1e293b" }}>
                        <input
                          type="text"
                          className="input-control"
                          style={{ flex: 1, fontSize: "0.82rem" }}
                          placeholder="Invia input allo stdin del processo..."
                          value={sendInputValue}
                          onChange={(e) => setSendInputValue(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === "Enter") {
                              e.preventDefault();
                              void handleSendInput(task.taskId);
                            }
                          }}
                        />
                        <button
                          type="button"
                          className="button button--primary button--small"
                          style={{ fontSize: "0.78rem" }}
                          onClick={() => void handleSendInput(task.taskId)}
                        >
                          Invia
                        </button>
                      </div>
                    )}

                    <div style={{ padding: "6px 14px", display: "flex", justifyContent: "flex-end" }}>
                      <button
                        type="button"
                        className="button button--secondary button--small"
                        style={{ fontSize: "0.75rem" }}
                        onClick={() => void fetchTaskLogs(task.taskId)}
                      >
                        🔄 Aggiorna Log
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
