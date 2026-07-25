import { useEffect, useState } from "react";
import { apiRequest } from "../api";
import type { AppLogLevel, LogEntry } from "../apiTypes/settings";

interface LogViewerModalProps {
  onClose: () => void;
  onLogsCleared?: () => void;
}

export function LogViewerModal({ onClose, onLogsCleared }: LogViewerModalProps) {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [filterLevel, setFilterLevel] = useState<string>("ALL");
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [isLoading, setIsLoading] = useState(false);
  const [copied, setCopied] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  useEffect(() => {
    void fetchLogs();
  }, [filterLevel, searchQuery]);

  async function fetchLogs() {
    setIsLoading(true);
    try {
      const params = new URLSearchParams();
      if (filterLevel !== "ALL") {
        params.append("minLevel", filterLevel);
      }
      if (searchQuery.trim()) {
        params.append("search", searchQuery.trim());
      }
      params.append("limit", "300");

      const query = params.toString() ? `?${params.toString()}` : "";
      const result = await apiRequest<LogEntry[]>(`/api/logs${query}`);
      setLogs(result);
    } catch {
      // Ignorato
    } finally {
      setIsLoading(false);
    }
  }

  async function handleClear() {
    if (!window.confirm("Cancellare ed azzerare tutti i log dal sistema?")) return;
    try {
      await apiRequest<{ success: boolean }>("/api/logs", { method: "DELETE" });
      setLogs([]);
      onLogsCleared?.();
    } catch {
      // Ignorato
    }
  }

  function handleCopy() {
    const text = logs.map((l) => {
      let line = `[${l.timestampUtc}] [${l.level}] [${l.category}] ${l.message}`;
      if (l.dataJson) line += ` | DATA: ${l.dataJson}`;
      if (l.exceptionDetails) line += `\n[EXCEPTION]\n${l.exceptionDetails}`;
      return line;
    }).join("\n");

    void navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  function getLevelBadgeStyle(level: AppLogLevel) {
    switch (level) {
      case "Error": return { bg: "#7f1d1d", color: "#fca5a5", border: "#ef4444" };
      case "Warning": return { bg: "#78350f", color: "#fde047", border: "#f59e0b" };
      case "Information": return { bg: "#065f46", color: "#6ee7b7", border: "#10b981" };
      case "Debug": return { bg: "#1e3a8a", color: "#93c5fd", border: "#3b82f6" };
      case "Trace": default: return { bg: "#312e81", color: "#c7d2fe", border: "#6366f1" };
    }
  }

  return (
    <div style={{
      position: "fixed",
      top: 0,
      left: 0,
      right: 0,
      bottom: 0,
      background: "rgba(15, 23, 42, 0.85)",
      backdropFilter: "blur(6px)",
      zIndex: 9999,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      padding: 20
    }}>
      <div style={{
        background: "#0f172a",
        border: "1px solid #334155",
        borderRadius: 12,
        width: "90%",
        maxWidth: 1100,
        height: "85vh",
        display: "flex",
        flexDirection: "column",
        boxShadow: "0 20px 50px rgba(0,0,0,0.6)",
        overflow: "hidden"
      }}>
        {/* HEADER MODALE */}
        <div style={{
          padding: "16px 20px",
          background: "#1e293b",
          borderBottom: "1px solid #334155",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between"
        }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <span style={{ fontSize: "1.3rem" }}>📜</span>
            <h2 style={{ margin: 0, color: "#f8fafc", fontSize: "1.1rem", fontWeight: 700 }}>
              Visualizzatore Log Live & Diagnostic Errors
            </h2>
            <span style={{ background: "#334155", color: "#cbd5e1", padding: "2px 8px", borderRadius: 12, fontSize: "0.78rem" }}>
              {logs.length} voci
            </span>
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <button
              type="button"
              onClick={handleCopy}
              disabled={logs.length === 0}
              style={{
                background: "#334155",
                color: "#f8fafc",
                border: "none",
                borderRadius: 6,
                padding: "6px 12px",
                fontSize: "0.82rem",
                fontWeight: 600,
                cursor: "pointer"
              }}
            >
              {copied ? "✅ Copiato!" : "📋 Copia Log"}
            </button>
            <button
              type="button"
              onClick={() => void fetchLogs()}
              disabled={isLoading}
              style={{
                background: "#334155",
                color: "#f8fafc",
                border: "none",
                borderRadius: 6,
                padding: "6px 12px",
                fontSize: "0.82rem",
                cursor: "pointer"
              }}
            >
              🔄 Aggiorna
            </button>
            <button
              type="button"
              onClick={() => void handleClear()}
              disabled={logs.length === 0}
              style={{
                background: "#7f1d1d",
                color: "#fecaca",
                border: "1px solid #ef4444",
                borderRadius: 6,
                padding: "6px 12px",
                fontSize: "0.82rem",
                cursor: "pointer"
              }}
            >
              🗑️ Svuota
            </button>
            <button
              type="button"
              onClick={onClose}
              style={{
                background: "transparent",
                color: "#94a3b8",
                border: "none",
                fontSize: "1.3rem",
                cursor: "pointer",
                padding: "0 8px"
              }}
            >
              ✕
            </button>
          </div>
        </div>

        {/* FILTRI */}
        <div style={{
          padding: "12px 20px",
          background: "#0f172a",
          borderBottom: "1px solid #1e293b",
          display: "flex",
          gap: 12,
          alignItems: "center"
        }}>
          <select
            value={filterLevel}
            onChange={(e) => setFilterLevel(e.target.value)}
            style={{
              background: "#1e293b",
              border: "1px solid #475569",
              color: "#f8fafc",
              padding: "6px 10px",
              borderRadius: 6,
              fontSize: "0.85rem"
            }}
          >
            <option value="ALL">Tutti i Livelli</option>
            <option value="Trace">Trace (Verboso)</option>
            <option value="Debug">Debug</option>
            <option value="Information">Information</option>
            <option value="Warning">Warning</option>
            <option value="Error">Error</option>
          </select>

          <input
            type="text"
            placeholder="Cerca nei log (es. AgentEngine, Ollama, Error)..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            style={{
              flex: 1,
              background: "#1e293b",
              border: "1px solid #475569",
              color: "#f8fafc",
              padding: "6px 12px",
              borderRadius: 6,
              fontSize: "0.85rem",
              outline: "none"
            }}
          />
        </div>

        {/* LISTA VOCI LOG */}
        <div style={{
          flex: 1,
          overflowY: "auto",
          padding: 16,
          display: "flex",
          flexDirection: "column",
          gap: 8,
          fontFamily: "monospace",
          fontSize: "0.83rem"
        }}>
          {isLoading && logs.length === 0 ? (
            <div style={{ color: "#94a3b8", textAlign: "center", padding: 40 }}>Caricamento log in corso...</div>
          ) : logs.length === 0 ? (
            <div style={{ color: "#64748b", textAlign: "center", padding: 40 }}>Nessun registro di log trovato.</div>
          ) : (
            logs.map((item) => {
              const badge = getLevelBadgeStyle(item.level);
              const isExpanded = expandedId === item.id;
              const hasExtra = Boolean(item.exceptionDetails || item.dataJson);

              return (
                <div
                  key={item.id}
                  style={{
                    background: "#1e293b",
                    border: "1px solid #334155",
                    borderRadius: 6,
                    padding: 10,
                    display: "flex",
                    flexDirection: "column",
                    gap: 6
                  }}
                >
                  <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
                    <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
                      <span style={{
                        background: badge.bg,
                        color: badge.color,
                        border: `1px solid ${badge.border}`,
                        padding: "2px 6px",
                        borderRadius: 4,
                        fontSize: "0.72rem",
                        fontWeight: 700,
                        textTransform: "uppercase"
                      }}>
                        {item.level}
                      </span>

                      <span style={{ color: "#38bdf8", fontWeight: 600 }}>
                        [{item.category}]
                      </span>

                      <span style={{ color: "#94a3b8", fontSize: "0.78rem" }}>
                        {new Date(item.timestampUtc).toLocaleTimeString()}
                      </span>
                    </div>

                    {hasExtra && (
                      <button
                        type="button"
                        onClick={() => setExpandedId(isExpanded ? null : item.id)}
                        style={{
                          background: "transparent",
                          border: "none",
                          color: "#60a5fa",
                          cursor: "pointer",
                          fontSize: "0.78rem"
                        }}
                      >
                        {isExpanded ? "▲ Nascondi Dettagli" : "▼ Dettagli / Eccezione"}
                      </button>
                    )}
                  </div>

                  <div style={{ color: "#e2e8f0", whiteSpace: "pre-wrap", wordBreak: "break-word" }}>
                    {item.message}
                  </div>

                  {isExpanded && (
                    <div style={{
                      marginTop: 8,
                      background: "#0f172a",
                      border: "1px solid #475569",
                      borderRadius: 4,
                      padding: 10,
                      color: "#fca5a5",
                      overflowX: "auto"
                    }}>
                      {item.dataJson && (
                        <div style={{ color: "#93c5fd", marginBottom: 6 }}>
                          📊 Dati: {item.dataJson}
                        </div>
                      )}
                      {item.exceptionDetails && (
                        <div>
                          ⚠️ Stack Trace / Dettaglio Eccezione:
                          <pre style={{ margin: "4px 0 0 0", color: "#fca5a5", fontSize: "0.78rem" }}>
                            {item.exceptionDetails}
                          </pre>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
