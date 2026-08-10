import { useEffect, useState, useMemo, useRef } from "react";
import { createPortal } from "react-dom";
import { apiRequest } from "../../api";
import type { AppLogLevel, LogEntry } from "../../apiTypes/settings";

import { ConfirmDialog } from "../common/ConfirmDialog";

interface LogViewerModalProps {
  onClose: () => void;
  onLogsCleared?: () => void;
}

export function LogViewerModal({ onClose, onLogsCleared }: LogViewerModalProps) {
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [filterLevel, setFilterLevel] = useState<string>("ALL");
  const [searchQuery, setSearchQuery] = useState<string>("");
  const [isLoading, setIsLoading] = useState(false);
  const [isLiveStreaming, setIsLiveStreaming] = useState(true);
  const [copied, setCopied] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [showConfirmClear, setShowConfirmClear] = useState(false);
  const listContainerRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") {
        onClose();
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [onClose]);

  useEffect(() => {
    void fetchLogs();

    if (!isLiveStreaming) return;

    let eventSource: EventSource | null = null;
    if (typeof window !== "undefined" && typeof window.EventSource !== "undefined") {
      const params = new URLSearchParams();
      if (filterLevel !== "ALL") {
        params.append("minLevel", filterLevel);
      }
      if (searchQuery.trim()) {
        params.append("search", searchQuery.trim());
      }
      const query = params.toString() ? `?${params.toString()}` : "";
      eventSource = new window.EventSource(`/api/logs/stream${query}`);
      eventSource.onmessage = (event) => {
        try {
          const entry = JSON.parse(event.data) as LogEntry;
          setLogs((previous) => {
            if (previous.some((existing) => existing.id === entry.id)) {
              return previous;
            }
            const next = [entry, ...previous];
            return next.slice(0, 300);
          });
          setError(null);
        } catch {
          // Ignore malformed stream payloads.
        }
      };
      eventSource.onerror = () => {
        setError("Il flusso live dei log è temporaneamente non disponibile. Riprova o usa l'aggiornamento manuale.");
      };
    }

    const interval = setInterval(() => {
      void fetchLogs(true);
    }, 2000);

    return () => {
      eventSource?.close();
      clearInterval(interval);
    };
  }, [filterLevel, searchQuery, isLiveStreaming]);

  async function fetchLogs(isSilent = false) {
    if (!isSilent) setIsLoading(true);
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
      setLogs(Array.isArray(result) ? result : []);
      setError(null);
    } catch (err) {
      if (!isSilent) {
        setError(err instanceof Error ? err.message : "Impossibile leggere i log dal backend locale.");
      }
    } finally {
      if (!isSilent) setIsLoading(false);
    }
  }

  async function executeClearLogs() {
    setShowConfirmClear(false);
    try {
      await apiRequest<{ success: boolean }>("/api/logs", { method: "DELETE" });
      setLogs([]);
      setError(null);
      onLogsCleared?.();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante la cancellazione dei log.");
    }
  }

  function handleCopy() {
    const text = logs
      .map((l) => {
        const levelName = normalizeLogLevel(l.level);
        let line = `[${l.timestampUtc}] [${levelName}] [${l.category}] ${l.message}`;
        if (l.dataJson) line += ` | DATA: ${l.dataJson}`;
        if (l.exceptionDetails) line += `\n[EXCEPTION]\n${l.exceptionDetails}`;
        return line;
      })
      .join("\n");

    void navigator.clipboard.writeText(text);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  const levelCounts = useMemo(() => {
    let errorCount = 0;
    let warningCount = 0;
    let infoCount = 0;
    let otherCount = 0;

    for (const log of logs) {
      const lvl = normalizeLogLevel(log.level);
      if (lvl === "Error") errorCount++;
      else if (lvl === "Warning") warningCount++;
      else if (lvl === "Information") infoCount++;
      else otherCount++;
    }

    return { errorCount, warningCount, infoCount, otherCount };
  }, [logs]);

  function normalizeLogLevel(rawLevel: unknown): AppLogLevel {
    if (typeof rawLevel === "number") {
      switch (rawLevel) {
        case 0: return "Trace";
        case 1: return "Debug";
        case 2: return "Information";
        case 3: return "Warning";
        case 4: return "Error";
        case 5: return "None";
        default: return "Information";
      }
    }
    if (typeof rawLevel === "string") {
      const s = rawLevel.trim().toLowerCase();
      if (s === "0" || s === "trace") return "Trace";
      if (s === "1" || s === "debug") return "Debug";
      if (s === "2" || s === "information" || s === "info") return "Information";
      if (s === "3" || s === "warning" || s === "warn") return "Warning";
      if (s === "4" || s === "error" || s === "err") return "Error";
      if (s === "5" || s === "none") return "None";
    }
    return "Information";
  }

  function getLevelBadgeStyle(rawLevel: unknown) {
    const level = normalizeLogLevel(rawLevel);
    switch (level) {
      case "Error": return { bg: "#7f1d1d", color: "#fca5a5", border: "#ef4444" };
      case "Warning": return { bg: "#78350f", color: "#fde047", border: "#f59e0b" };
      case "Information": return { bg: "#065f46", color: "#6ee7b7", border: "#10b981" };
      case "Debug": return { bg: "#1e3a8a", color: "#93c5fd", border: "#3b82f6" };
      case "Trace": default: return { bg: "#312e81", color: "#c7d2fe", border: "#6366f1" };
    }
  }

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="log-viewer-modal-title"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
      style={{
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
      }}
    >
      <div
        onClick={(e) => e.stopPropagation()}
        style={{
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
        }}
      >
        {/* HEADER MODALE */}
        <div style={{
          padding: "16px 20px",
          background: "#1e293b",
          borderBottom: "1px solid #334155",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between"
        }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
            <span style={{ fontSize: "1.3rem" }}>📜</span>
            <h2 id="log-viewer-modal-title" style={{ margin: 0, color: "#f8fafc", fontSize: "1.1rem", fontWeight: 700 }}>
              Visualizzatore Log Live &amp; Diagnostica
            </h2>
            <span style={{ background: "#334155", color: "#cbd5e1", padding: "2px 8px", borderRadius: 12, fontSize: "0.78rem" }}>
              {logs.length} voci
            </span>
            {levelCounts.errorCount > 0 && (
              <span style={{ background: "#7f1d1d", color: "#fca5a5", border: "1px solid #ef4444", padding: "2px 8px", borderRadius: 12, fontSize: "0.78rem", fontWeight: 700 }}>
                {levelCounts.errorCount} Errori
              </span>
            )}
            {levelCounts.warningCount > 0 && (
              <span style={{ background: "#78350f", color: "#fde047", border: "1px solid #f59e0b", padding: "2px 8px", borderRadius: 12, fontSize: "0.78rem", fontWeight: 700 }}>
                {levelCounts.warningCount} Avvisi
              </span>
            )}
            <button
              type="button"
              onClick={() => setIsLiveStreaming((prev) => !prev)}
              style={{
                background: isLiveStreaming ? "rgba(16, 185, 129, 0.2)" : "#334155",
                color: isLiveStreaming ? "#34d399" : "#cbd5e1",
                border: isLiveStreaming ? "1px solid #10b981" : "1px solid #475569",
                borderRadius: 12,
                padding: "2px 10px",
                fontSize: "0.78rem",
                fontWeight: 600,
                cursor: "pointer",
                display: "inline-flex",
                alignItems: "center",
                gap: 4
              }}
              title={isLiveStreaming ? "Pausa aggiornamento automatico" : "Attiva aggiornamento live ogni 2s"}
            >
              {isLiveStreaming ? "🟢 Live Attivo (2s)" : "⏸️ Live Pausato"}
            </button>
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
              onClick={() => setShowConfirmClear(true)}
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
            <option value="Debug">Debug</option>
            <option value="Information">Info</option>
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

        {error && (
          <div style={{
            background: "#450a0a",
            color: "#fca5a5",
            borderBottom: "1px solid #ef4444",
            padding: "8px 20px",
            fontSize: "0.83rem",
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between"
          }}>
            <span>⚠️ {error}</span>
            <button
              type="button"
              onClick={() => void fetchLogs()}
              style={{
                background: "#7f1d1d",
                color: "#fff",
                border: "none",
                borderRadius: 4,
                padding: "2px 8px",
                fontSize: "0.78rem",
                cursor: "pointer"
              }}
            >
              Riprova
            </button>
          </div>
        )}

        {/* LISTA VOCI LOG */}
        <div
          ref={listContainerRef}
          style={{
            flex: 1,
            overflowY: "auto",
            padding: 16,
            display: "flex",
            flexDirection: "column",
            gap: 8,
            fontFamily: "monospace",
            fontSize: "0.83rem"
          }}
        >
          {isLoading && logs.length === 0 ? (
            <div style={{ color: "#94a3b8", textAlign: "center", padding: 40 }}>Caricamento log in corso...</div>
          ) : logs.length === 0 ? (
            <div style={{ color: "#64748b", textAlign: "center", padding: 40 }}>Nessun registro di log trovato.</div>
          ) : (
            logs.map((item) => {
              const normalizedLevel = normalizeLogLevel(item.level);
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
                        {normalizedLevel}
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

      <ConfirmDialog
        isOpen={showConfirmClear}
        title="Cancellazione Log"
        message="Cancellare ed azzerare tutti i log dal sistema?"
        confirmLabel="Svuota Log"
        cancelLabel="Annulla"
        variant="danger"
        onConfirm={() => void executeClearLogs()}
        onCancel={() => setShowConfirmClear(false)}
      />
    </div>,
    document.body
  );
}
