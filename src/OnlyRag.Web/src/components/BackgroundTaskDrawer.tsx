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
        className="modal-content animate-fade-in max-w-[700px] max-h-[80vh] flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal-header flex justify-between items-center">
          <h3 className="m-0 flex items-center gap-2 text-base font-semibold">
            <span>💻</span> Processi in Background ({tasks.length})
          </h3>
          <button type="button" className="button-secondary" onClick={onClose}>✕</button>
        </div>

        <div className="modal-body flex-1 overflow-y-auto flex flex-col gap-2.5">
          {tasks.length === 0 ? (
            <div className="text-center text-slate-500 py-8">
              <div className="text-3xl mb-2">📋</div>
              <p>Nessun processo in background attivo.</p>
            </div>
          ) : (
            tasks.map((task) => (
              <div
                key={task.taskId}
                className={`bg-slate-900 rounded-lg overflow-hidden border ${
                  task.isRunning ? "border-blue-500" : "border-slate-700"
                }`}
              >
                {/* Task header */}
                <div
                  className="flex items-center justify-between px-3.5 py-2.5 cursor-pointer hover:bg-slate-800/50 transition-colors"
                  onClick={() => handleToggleExpand(task.taskId)}
                >
                  <div className="flex items-center gap-2.5">
                    <span className="text-sm">
                      {task.isRunning ? "🟢" : (task.exitCode === 0 ? "✅" : "❌")}
                    </span>
                    <div>
                      <div className="font-mono text-xs text-slate-200 font-semibold">
                        {task.command.length > 60 ? task.command.slice(0, 60) + "…" : task.command}
                      </div>
                      <div className="text-[11px] text-slate-400 mt-0.5">
                        ID: {task.taskId} · Avviato: {new Date(task.startedAt).toLocaleTimeString()}
                        {!task.isRunning && task.exitCode !== null && ` · Exit: ${task.exitCode}`}
                      </div>
                    </div>
                  </div>
                  <div className="flex gap-1.5 items-center">
                    {task.isRunning && (
                      <button
                        type="button"
                        className="button button--small bg-red-950 border-red-500 text-red-300 text-xs px-2 py-0.5 rounded-md hover:bg-red-900"
                        onClick={(e) => { e.stopPropagation(); void handleKillTask(task.taskId); }}
                      >
                        ⬛ Kill
                      </button>
                    )}
                    <span className="text-slate-500 text-xs">
                      {expandedTaskId === task.taskId ? "▲" : "▼"}
                    </span>
                  </div>
                </div>

                {/* Expanded logs */}
                {expandedTaskId === task.taskId && (
                  <div className="border-t border-slate-800">
                    <pre className="m-0 p-3 bg-slate-950 text-[11px] text-slate-300 max-h-[250px] overflow-y-auto whitespace-pre-wrap font-mono">
                      {taskLogs[task.taskId] || "Caricamento log..."}
                    </pre>

                    {task.isRunning && (
                      <div className="flex gap-1.5 p-3 border-t border-slate-800">
                        <input
                          type="text"
                          className="input-control flex-1 text-xs"
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
                          className="button button--primary button--small text-xs"
                          onClick={() => void handleSendInput(task.taskId)}
                        >
                          Invia
                        </button>
                      </div>
                    )}

                    <div className="p-2 flex justify-end">
                      <button
                        type="button"
                        className="button button--secondary button--small text-xs"
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

