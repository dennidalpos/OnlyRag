import { useState } from "react";
import type { AgentStepEvent } from "../api";

type AgentToolCallCardProps = {
  event: AgentStepEvent;
  onApprove?: (callId: string, approved: boolean) => void;
};

const TOOL_ICONS: Record<string, string> = {
  list_dir: "DIR",
  read_file: "FILE",
  view_file: "FILE",
  write_file: "WRITE",
  write_to_file: "WRITE",
  replace_file_content: "EDIT",
  grep_search: "SEARCH",
  run_command: "CMD",
  invoke_subagent: "SUBAGENT",
  manage_task: "TASK"
};

function formatArgsSummary(toolName: string, argsJson: string): string {
  try {
    const args = JSON.parse(argsJson) as Record<string, unknown>;
    if (toolName === "list_dir") return `${args.relativePath || "."}`;
    if (toolName === "read_file" || toolName === "view_file") {
      const range = args.startLine ? ` (L${args.startLine}-${args.endLine || "..."})` : "";
      return `${args.relativePath}${range}`;
    }
    if (toolName === "write_file" || toolName === "write_to_file") return `${args.relativePath} (${(String(args.content ?? "")).length} car.)`;
    if (toolName === "replace_file_content") return `${args.relativePath}`;
    if (toolName === "grep_search") return `"${args.query}" in ${args.searchPath || "."}`;
    if (toolName === "run_command") return `${args.commandLine}`;
    if (toolName === "invoke_subagent") return `[${args.role || "Subagente"}] ${args.prompt || ""}`;
    if (toolName === "manage_task") return `Action: ${args.action || "list"} (Task: ${args.taskId || "all"})`;
    return JSON.stringify(args).slice(0, 120);
  } catch {
    return argsJson.slice(0, 120);
  }
}

export function AgentToolCallCard({ event, onApprove }: AgentToolCallCardProps) {
  const [expanded, setExpanded] = useState(false);

  if (event.type === "thought" || event.type === "thought_chunk") {
    const isStepThought = event.content?.includes("[Agent Step") || event.content?.includes("Elaborazione");
    return (
      <div className="agent-step agent-step--thought" style={{
        display: "flex", alignItems: "flex-start", gap: 10, padding: "10px 14px",
        background: isStepThought ? "#1e1b4b" : "#0f172a", borderRadius: 8,
        border: isStepThought ? "1px solid #6366f1" : "1px solid #334155",
        fontSize: "0.86rem", color: isStepThought ? "#c7d2fe" : "#cbd5e1",
        whiteSpace: "pre-wrap", boxShadow: isStepThought ? "0 0 12px rgba(99,102,241,0.15)" : "none"
      }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#818cf8" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <circle cx="12" cy="12" r="10" />
          <polyline points="12 6 12 12 16 14" />
        </svg>
        <div style={{ flex: 1 }}>
          {isStepThought && (
            <div style={{ fontSize: "0.78rem", fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", color: "#818cf8", marginBottom: 2 }}>
              Pensiero & Inizializzazione LLM
            </div>
          )}
          <span>{event.content}</span>
        </div>
      </div>
    );
  }

  if (event.type === "tool_proposed" && event.toolCall) {
    const tc = event.toolCall;
    const badgeText = TOOL_ICONS[tc.toolName] || "TOOL";
    const summary = formatArgsSummary(tc.toolName, tc.argumentsJson);

    return (
      <div className="agent-step agent-step--tool" style={{
        padding: "10px 14px", background: "#111827", borderRadius: 8,
        border: tc.requiresApproval ? "1px solid #f59e0b" : "1px solid #334155",
        boxShadow: tc.requiresApproval ? "0 0 12px rgba(245,158,11,0.15)" : "none"
      }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 10 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <span style={{ fontSize: "0.7rem", fontWeight: 700, padding: "2px 6px", borderRadius: 4, background: "#1e293b", color: "#818cf8", border: "1px solid #334155" }}>
              {badgeText}
            </span>
            <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.88rem" }}>{tc.toolName}</span>
            <span style={{ color: "#94a3b8", fontSize: "0.82rem", fontFamily: "monospace" }}>{summary}</span>
          </div>
          <div style={{ display: "flex", gap: 6, alignItems: "center" }}>
            {tc.requiresApproval && onApprove && (
              <>
                <button
                  type="button"
                  className="button button--small"
                  style={{ background: "#065f46", borderColor: "#10b981", color: "#ecfdf5", fontSize: "0.78rem", padding: "4px 10px", borderRadius: 6 }}
                  onClick={() => onApprove(tc.callId, true)}
                >
                  Approva
                </button>
                <button
                  type="button"
                  className="button button--small"
                  style={{ background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5", fontSize: "0.78rem", padding: "4px 10px", borderRadius: 6 }}
                  onClick={() => onApprove(tc.callId, false)}
                >
                  Rifiuta
                </button>
              </>
            )}
            <button
              type="button"
              style={{ background: "none", border: "none", color: "#64748b", cursor: "pointer", fontSize: "0.8rem" }}
              onClick={() => setExpanded(!expanded)}
            >
              {expanded ? "Nascondi" : "Dettagli"}
            </button>
          </div>
        </div>

        {tc.explanation && (
          <div style={{ marginTop: 6, fontSize: "0.82rem", color: "#94a3b8" }}>
            {tc.explanation}
          </div>
        )}

        {expanded && (
          <pre style={{
            marginTop: 8, padding: 10, background: "#090d16", borderRadius: 6,
            border: "1px solid #1e293b", fontSize: "0.78rem", color: "#cbd5e1",
            overflow: "auto", maxHeight: 200, whiteSpace: "pre-wrap"
          }}>
            {(() => {
              try {
                return JSON.stringify(JSON.parse(tc.argumentsJson), null, 2);
              } catch {
                return tc.argumentsJson;
              }
            })()}
          </pre>
        )}
      </div>
    );
  }

  if (event.type === "tool_result" && event.toolResult) {
    const tr = event.toolResult;
    const previewLen = 300;
    const outputPreview = tr.output.length > previewLen ? tr.output.slice(0, previewLen) + "..." : tr.output;

    return (
      <div className="agent-step agent-step--result" style={{
        padding: "8px 12px", background: tr.success ? "#0a1a0e" : "#1a0a0a",
        borderRadius: 8, border: tr.success ? "1px solid #16a34a33" : "1px solid #dc262633"
      }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <span style={{ fontSize: "0.75rem", fontWeight: 700, color: tr.success ? "#4ade80" : "#f87171" }}>
              {tr.success ? "OK" : "ERR"}
            </span>
            <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.85rem" }}>{tr.toolName}</span>
            {tr.error && <span style={{ color: "#fca5a5", fontSize: "0.82rem" }}>— {tr.error}</span>}
          </div>
          <button
            type="button"
            style={{ background: "none", border: "none", color: "#64748b", cursor: "pointer", fontSize: "0.8rem" }}
            onClick={() => setExpanded(!expanded)}
          >
            {expanded ? "Nascondi" : "Output"}
          </button>
        </div>

        {(expanded || tr.output.length <= previewLen) && tr.output && (
          <pre style={{
            marginTop: 6, padding: 8, background: "#090d16", borderRadius: 6,
            border: "1px solid #1e293b", fontSize: "0.78rem", color: "#cbd5e1",
            overflow: "auto", maxHeight: 300, whiteSpace: "pre-wrap"
          }}>
            {expanded ? tr.output : outputPreview}
          </pre>
        )}
      </div>
    );
  }

  if (event.type === "approval_required" && event.toolCall) {
    return (
      <div className="agent-step agent-step--approval" style={{
        padding: "12px 14px", background: "#1c1917", borderRadius: 8,
        border: "1px solid #f59e0b", boxShadow: "0 0 16px rgba(245,158,11,0.2)"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
            <line x1="12" y1="9" x2="12" y2="13" />
            <line x1="12" y1="17" x2="12.01" y2="17" />
          </svg>
          <span style={{ fontWeight: 700, color: "#fbbf24", fontSize: "0.92rem" }}>
            Approvazione richiesta per esecuzione comando
          </span>
        </div>
        <pre style={{
          padding: 10, background: "#090d16", borderRadius: 6, border: "1px solid #334155",
          fontSize: "0.85rem", color: "#f8fafc", whiteSpace: "pre-wrap"
        }}>
          {(() => {
            try {
              const args = JSON.parse(event.toolCall!.argumentsJson) as Record<string, unknown>;
              return String(args.commandLine ?? event.toolCall!.argumentsJson);
            } catch {
              return event.toolCall!.argumentsJson;
            }
          })()}
        </pre>
        {onApprove && (
          <div style={{ display: "flex", gap: 10, marginTop: 10 }}>
            <button
              type="button"
              className="button button--primary"
              style={{ background: "#065f46", borderColor: "#10b981", color: "#ecfdf5" }}
              onClick={() => onApprove(event.toolCall!.callId, true)}
            >
              Esegui Comando su Windows
            </button>
            <button
              type="button"
              className="button button--secondary"
              style={{ background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5" }}
              onClick={() => onApprove(event.toolCall!.callId, false)}
            >
              Rifiuta Esecuzione
            </button>
          </div>
        )}
      </div>
    );
  }

  if (event.type === "final_response" && event.content) {
    return (
      <div className="agent-step agent-step--final" style={{
        padding: "12px 16px", background: "#1e293b", borderRadius: 10,
        border: "1px solid #334155", boxShadow: "0 2px 8px rgba(0,0,0,0.2)"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#6366f1" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <rect x="3" y="11" width="18" height="10" rx="2" />
            <circle cx="12" cy="5" r="2" />
            <path d="M12 7v4" />
            <line x1="8" y1="16" x2="8" y2="16" />
            <line x1="16" y1="16" x2="16" y2="16" />
          </svg>
          <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.88rem" }}>Risposta Agente</span>
        </div>
        <div style={{
          whiteSpace: "pre-wrap", fontFamily: "monospace", fontSize: "0.88rem",
          lineHeight: 1.6, color: "#f8fafc"
        }}>
          {event.content}
        </div>
      </div>
    );
  }

  if (event.type === "json_parse_warning") {
    return (
      <div className="agent-step agent-step--warning" style={{
        display: "flex", alignItems: "flex-start", gap: 10, padding: "10px 14px",
        background: "linear-gradient(135deg, #451a03 0%, #1c0a00 100%)", borderRadius: 8,
        border: "1px solid #f59e0b", fontSize: "0.86rem", color: "#fcd34d",
        boxShadow: "0 0 12px rgba(245,158,11,0.15)"
      }}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
          <line x1="12" y1="9" x2="12" y2="13" />
          <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: "0.78rem", fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", color: "#f59e0b", marginBottom: 2 }}>
            Correzione Formato JSON
          </div>
          <span style={{ color: "#fde68a" }}>{event.content}</span>
        </div>
      </div>
    );
  }

  if (event.type === "error") {
    return (
      <div className="agent-step agent-step--error" style={{
        padding: "12px 14px", background: "linear-gradient(135deg, #450a0a 0%, #180505 100%)",
        borderRadius: 8, border: "1px solid #ef4444", color: "#fca5a5", fontSize: "0.88rem",
        boxShadow: "0 0 12px rgba(239,68,68,0.2)"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
          <span style={{ fontSize: "1.2rem" }}>⚠️</span>
          <strong style={{ color: "#f87171", fontSize: "0.9rem" }}>Errore durante l'esecuzione della Modalità Agente</strong>
        </div>
        <div style={{ whiteSpace: "pre-wrap", fontFamily: "monospace", fontSize: "0.84rem", color: "#fecaca" }}>
          {event.content}
        </div>
        <div style={{ marginTop: 8, fontSize: "0.78rem", color: "#94a3b8" }}>
          ℹ️ Consulta la sezione <strong>Impostazioni &gt; Sistema di Log &amp; Diagnostic Debug</strong> per l'analisi dettagliata.
        </div>
      </div>
    );
  }

  return null;
}
