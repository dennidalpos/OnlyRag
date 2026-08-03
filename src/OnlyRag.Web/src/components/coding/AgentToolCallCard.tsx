import {
  AlertTriangle,
  Bot,
  Brain,
  Check,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Layers,
  Sparkles,
  X,
  XCircle
} from "lucide-react";
import { useState } from "react";
import type { AgentStepEvent } from "../../api";
import { PlanChecklistVisualizer } from "./PlanChecklistVisualizer";
import { UnifiedDiffViewer } from "./UnifiedDiffViewer";

type AgentToolCallCardProps = {
  event: AgentStepEvent;
  onApprove?: (callId: string, approved: boolean) => void;
};

const TOOL_BADGES: Record<string, string> = {
  list_dir: "DIR",
  read_file: "FILE",
  view_file: "FILE",
  write_file: "WRITE",
  write_to_file: "WRITE",
  replace_file_content: "EDIT",
  multi_replace_file_content: "MULTI-EDIT",
  grep_search: "SEARCH",
  git_diff_inspect: "GIT",
  run_command: "CMD",
  web_search: "WEB",
  search_web: "WEB",
  query_retrieval_index: "RAG",
  rag_hybrid_search: "RAG",
  plan_task: "PLAN",
  reflect_step: "REFLECT",
  ingest_office_doc: "INGEST",
  generate_image_onnx: "IMG-GEN",
  ast_structural_refactor: "AST",
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
    if (toolName === "multi_replace_file_content") {
      const chunks = Array.isArray(args.chunks) ? args.chunks.length : "?";
      return `${args.relativePath} (${chunks} chunks)`;
    }
    if (toolName === "grep_search") return `"${args.query}" in ${args.searchPath || "."}`;
    if (toolName === "git_diff_inspect") return args.relativePath ? `${args.relativePath}` : "workspace";
    if (toolName === "run_command") return `${args.commandLine}`;
    if (toolName === "web_search" || toolName === "search_web") return `"${args.query}" ${args.domain ? `su ${args.domain}` : "(fonti ufficiali)"}`;
    if (toolName === "query_retrieval_index" || toolName === "rag_hybrid_search") return `"${args.query}" (top${args.topK ?? 5})`;
    if (toolName === "plan_task") {
      const steps = Array.isArray(args.steps) ? args.steps.length : "?";
      return `${steps} passi pianificati`;
    }
    if (toolName === "reflect_step") return `[${args.stepId ?? "-"}] ${args.status ?? ""}: ${String(args.learnings ?? "").slice(0, 60)}`;
    if (toolName === "ingest_office_doc") return `${args.relativePath}${args.forceOcr ? " (force-OCR)" : ""}`;
    if (toolName === "generate_image_onnx") return `"${String(args.prompt ?? "").slice(0, 60)}" (${args.aspectRatio ?? "1:1"})`;
    if (toolName === "ast_structural_refactor") return `${args.operation}: ${args.targetSymbol} → ${args.newSymbolName ?? "?"}` ;
    if (toolName === "invoke_subagent") return `[${args.role || "Subagente"}] ${args.prompt || ""}`;
    if (toolName === "manage_task") return `Action: ${args.action || "list"} (Task: ${args.taskId || "all"})`;
    return JSON.stringify(args).slice(0, 120);
  } catch {
    return argsJson.slice(0, 120);
  }
}

function cleanThoughtContent(text?: string | null): string {
  if (!text) return "";
  if (!text.includes("```") && !text.includes("<tool") && !text.includes("<function_call") && !text.includes("{")) {
    return text.trim();
  }
  let cleaned = text.replace(/```(?:json|JSON)?\s*[\s\S]*?(?:```|$)/g, "");
  cleaned = cleaned.replace(/<(?:tool_call|tool|function_call)>[\s\S]*?(?:<\/(?:tool_call|tool|function_call)>|$)/g, "");
  return cleaned.trim();
}

export function AgentToolCallCard({ event, onApprove }: AgentToolCallCardProps) {
  const [isDetailsExpanded, setIsDetailsExpanded] = useState(false);
  const [isOutputVisible, setIsOutputVisible] = useState<boolean | null>(null);

  if (event.type === "thought" || event.type === "thought_chunk") {
    const isStepThought = event.content?.includes("[Agent Step") || event.content?.includes("Elaborazione");
    const cleanedText = isStepThought ? event.content : cleanThoughtContent(event.content);
    if (!cleanedText) return null;

    return (
      <div className="agent-step agent-step--thought" style={{
        display: "flex", alignItems: "flex-start", gap: 10, padding: "10px 14px",
        background: isStepThought ? "#1e1b4b" : "#0f172a", borderRadius: 8,
        border: isStepThought ? "1px solid #6366f1" : "1px solid #334155",
        fontSize: "0.86rem", color: isStepThought ? "#c7d2fe" : "#cbd5e1",
        whiteSpace: "pre-wrap", overflowWrap: "anywhere", wordBreak: "break-word",
        boxShadow: isStepThought ? "0 0 12px rgba(99,102,241,0.15)" : "none"
      }}>
        <Brain size={18} style={{ color: "#818cf8", flexShrink: 0, marginTop: 2 }} />
        <div style={{ flex: 1, minWidth: 0 }}>
          {isStepThought && (
            <div style={{ fontSize: "0.78rem", fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", color: "#818cf8", marginBottom: 2, display: "flex", alignItems: "center", gap: 6 }}>
              <Sparkles size={13} /> Pensiero &amp; Inizializzazione LLM
            </div>
          )}
          <span style={{ overflowWrap: "anywhere", wordBreak: "break-word" }}>{cleanedText}</span>
        </div>
      </div>
    );
  }

  if (event.type === "tool_proposed" && event.toolCall) {
    const tc = event.toolCall;
    const badgeText = TOOL_BADGES[tc.toolName] || "TOOL";
    const summary = formatArgsSummary(tc.toolName, tc.argumentsJson);

    return (
      <div className="agent-step agent-step--tool" style={{
        padding: "10px 14px", background: "#111827", borderRadius: 8,
        border: tc.requiresApproval ? "1px solid #f59e0b" : "1px solid #334155",
        boxShadow: tc.requiresApproval ? "0 0 12px rgba(245,158,11,0.15)" : "none",
        display: "flex", flexDirection: "column", gap: 8
      }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 8 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap", minWidth: 0, flex: 1 }}>
            <span style={{ fontSize: "0.7rem", fontWeight: 700, padding: "2px 6px", borderRadius: 4, background: "#1e293b", color: "#818cf8", border: "1px solid #334155", flexShrink: 0 }}>
              {badgeText}
            </span>
            <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.88rem", flexShrink: 0 }}>{tc.toolName}</span>
            <span style={{ color: "#94a3b8", fontSize: "0.82rem", fontFamily: "monospace", overflowWrap: "anywhere", wordBreak: "break-word", minWidth: 0, flex: 1 }}>
              {summary}
            </span>
          </div>
          <div style={{ display: "flex", gap: 6, alignItems: "center", flexShrink: 0 }}>
            {tc.requiresApproval && onApprove && (
              <>
                <button
                  type="button"
                  className="button button--small"
                  style={{ background: "#065f46", borderColor: "#10b981", color: "#ecfdf5", fontSize: "0.78rem", padding: "4px 10px", borderRadius: 6, display: "inline-flex", alignItems: "center", gap: 4 }}
                  onClick={() => onApprove(tc.callId, true)}
                >
                  <Check size={14} /> Approva
                </button>
                <button
                  type="button"
                  className="button button--small"
                  style={{ background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5", fontSize: "0.78rem", padding: "4px 10px", borderRadius: 6, display: "inline-flex", alignItems: "center", gap: 4 }}
                  onClick={() => onApprove(tc.callId, false)}
                >
                  <X size={14} /> Rifiuta
                </button>
              </>
            )}
            <button
              type="button"
              style={{ background: "none", border: "none", color: "#64748b", cursor: "pointer", fontSize: "0.8rem", display: "inline-flex", alignItems: "center", gap: 4 }}
              onClick={() => setIsDetailsExpanded(!isDetailsExpanded)}
            >
              {isDetailsExpanded ? <><ChevronUp size={14} /> Nascondi</> : <><ChevronDown size={14} /> Dettagli</>}
            </button>
          </div>
        </div>

        {tc.explanation && (
          <div style={{ fontSize: "0.82rem", color: "#94a3b8", overflowWrap: "anywhere", wordBreak: "break-word" }}>
            {tc.explanation}
          </div>
        )}

        {(tc.toolName === "replace_file_content" || tc.toolName === "multi_replace_file_content") && (
          (() => {
            try {
              const args = JSON.parse(tc.argumentsJson) as { targetContent?: string; replacementContent?: string };
              if (args.targetContent || args.replacementContent) {
                return (
                  <div style={{ marginTop: 6, padding: 8, background: "#090d16", borderRadius: 6, border: "1px solid #1e293b", fontFamily: "monospace", fontSize: "0.78rem" }}>
                    <div style={{ color: "#f87171", backgroundColor: "rgba(239, 68, 68, 0.1)", padding: "4px 8px", borderRadius: 4, marginBottom: 4, whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
                      - {args.targetContent}
                    </div>
                    <div style={{ color: "#4ade80", backgroundColor: "rgba(34, 197, 94, 0.1)", padding: "4px 8px", borderRadius: 4, whiteSpace: "pre-wrap", overflowWrap: "anywhere" }}>
                      + {args.replacementContent}
                    </div>
                  </div>
                );
              }
            } catch {
              return null;
            }
            return null;
          })()
        )}

        {isDetailsExpanded && (
          <pre style={{
            marginTop: 4, padding: 10, background: "#090d16", borderRadius: 6,
            border: "1px solid #1e293b", fontSize: "0.78rem", color: "#cbd5e1",
            overflowX: "auto", maxHeight: 200, whiteSpace: "pre-wrap", overflowWrap: "anywhere", wordBreak: "break-word"
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

  if (event.type === "plan_update" && event.planMarkdown) {
    return <PlanChecklistVisualizer planMarkdown={event.planMarkdown} />;
  }

  if (event.type === "batch_tools_proposed" && event.batchToolCalls) {
    return (
      <div className="agent-step agent-step--batch" style={{
        padding: "10px 14px", background: "#0f172a", borderRadius: 8,
        border: "1px solid #38bdf8", boxShadow: "0 0 12px rgba(56, 189, 248, 0.15)"
      }}>
        <div style={{ fontSize: "0.82rem", fontWeight: 700, color: "#38bdf8", textTransform: "uppercase", marginBottom: 6, display: "flex", alignItems: "center", gap: 6 }}>
          <Layers size={16} /> Chiamata Parallela Multi-Tool ({event.batchToolCalls.length} strumenti)
        </div>
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          {event.batchToolCalls.map((tc, idx) => (
            <div key={idx} style={{ fontSize: "0.84rem", color: "#e2e8f0" }}>
              • <strong style={{ color: "#38bdf8" }}>{tc.toolName}</strong>: {formatArgsSummary(tc.toolName, tc.argumentsJson)}
            </div>
          ))}
        </div>
      </div>
    );
  }

  if (event.type === "tool_result" && event.toolResult) {
    const tr = event.toolResult;
    const previewLen = 300;
    const isOutputLong = tr.output.length > previewLen;
    const isVisible = isOutputVisible !== null ? isOutputVisible : !isOutputLong;

    return (
      <div className="agent-step agent-step--result" style={{
        padding: "8px 12px", background: tr.success ? "#0a1a0e" : "#1a0a0a",
        borderRadius: 8, border: tr.success ? "1px solid #16a34a33" : "1px solid #dc262633"
      }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", flexWrap: "wrap", gap: 8 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 6, flexWrap: "wrap", minWidth: 0, flex: 1 }}>
            {tr.success ? (
              <CheckCircle2 size={16} style={{ color: "#4ade80", flexShrink: 0 }} />
            ) : (
              <XCircle size={16} style={{ color: "#f87171", flexShrink: 0 }} />
            )}
            <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.85rem", flexShrink: 0 }}>{tr.toolName}</span>
            {tr.error && <span style={{ color: "#fca5a5", fontSize: "0.82rem", overflowWrap: "anywhere", wordBreak: "break-word" }}>— {tr.error}</span>}
          </div>
          {tr.output && (
            <button
              type="button"
              style={{ background: "none", border: "none", color: "#64748b", cursor: "pointer", fontSize: "0.8rem", flexShrink: 0, display: "inline-flex", alignItems: "center", gap: 4 }}
              onClick={() => setIsOutputVisible(!isVisible)}
            >
              {isVisible ? <><ChevronUp size={14} /> Nascondi</> : <><ChevronDown size={14} /> Output</>}
            </button>
          )}
        </div>

        {tr.diffPatch && <UnifiedDiffViewer patch={tr.diffPatch} />}

        {isVisible && tr.output && (
          <pre style={{
            marginTop: 6, padding: 8, background: "#090d16", borderRadius: 6,
            border: "1px solid #1e293b", fontSize: "0.78rem", color: "#cbd5e1",
            overflowX: "auto", maxHeight: 300, whiteSpace: "pre-wrap", overflowWrap: "anywhere", wordBreak: "break-word"
          }}>
            {tr.output}
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
          <AlertTriangle size={18} style={{ color: "#f59e0b", flexShrink: 0 }} />
          <span style={{ fontWeight: 700, color: "#fbbf24", fontSize: "0.92rem" }}>
            Approvazione richiesta per esecuzione comando
          </span>
        </div>
        <pre style={{
          padding: 10, background: "#090d16", borderRadius: 6, border: "1px solid #334155",
          fontSize: "0.85rem", color: "#f8fafc", whiteSpace: "pre-wrap", overflowWrap: "anywhere", wordBreak: "break-word"
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
          <div style={{ display: "flex", gap: 10, marginTop: 10, flexWrap: "wrap" }}>
            <button
              type="button"
              className="button button--primary"
              style={{ background: "#065f46", borderColor: "#10b981", color: "#ecfdf5", display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={() => onApprove(event.toolCall!.callId, true)}
            >
              <Check size={16} /> Esegui Comando su Windows
            </button>
            <button
              type="button"
              className="button button--secondary"
              style={{ background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5", display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={() => onApprove(event.toolCall!.callId, false)}
            >
              <X size={16} /> Rifiuta Esecuzione
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
          <Bot size={18} style={{ color: "#6366f1", flexShrink: 0 }} />
          <span style={{ fontWeight: 600, color: "#e2e8f0", fontSize: "0.88rem" }}>Risposta Agente</span>
        </div>
        <div style={{
          whiteSpace: "pre-wrap", fontFamily: "monospace", fontSize: "0.88rem",
          lineHeight: 1.6, color: "#f8fafc", overflowWrap: "anywhere", wordBreak: "break-word"
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
        boxShadow: "0 0 12px rgba(245,158,11,0.15)", overflowWrap: "anywhere", wordBreak: "break-word"
      }}>
        <AlertTriangle size={18} style={{ color: "#f59e0b", flexShrink: 0, marginTop: 2 }} />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: "0.78rem", fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", color: "#f59e0b", marginBottom: 2 }}>
            Correzione Formato JSON
          </div>
          <span style={{ color: "#fde68a", overflowWrap: "anywhere", wordBreak: "break-word" }}>{event.content}</span>
        </div>
      </div>
    );
  }

  if (event.type === "error") {
    return (
      <div className="agent-step agent-step--error" style={{
        padding: "12px 14px", background: "linear-gradient(135deg, #450a0a 0%, #180505 100%)",
        borderRadius: 8, border: "1px solid #ef4444", color: "#fca5a5", fontSize: "0.88rem",
        boxShadow: "0 0 12px rgba(239,68,68,0.2)", overflowWrap: "anywhere", wordBreak: "break-word"
      }}>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
          <AlertTriangle size={18} style={{ color: "#f87171", flexShrink: 0 }} />
          <strong style={{ color: "#f87171", fontSize: "0.9rem" }}>Errore durante l'esecuzione della Modalità Agente</strong>
        </div>
        <div style={{ whiteSpace: "pre-wrap", fontFamily: "monospace", fontSize: "0.84rem", color: "#fecaca", overflowWrap: "anywhere", wordBreak: "break-word" }}>
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
