import {
  AlertTriangle,
  ArrowDown,
  Bot,
  CheckCircle2,
  ExternalLink,
  FileCode,
  FileDiff,
  Loader2,
  Save,
  Sparkles,
  Trash2,
  User
} from "lucide-react";
import type { RefObject } from "react";
import { AgentToolCallCard } from "./AgentToolCallCard";
import { ReasoningTraceVisualizer } from "./ReasoningTraceVisualizer";
import type { CodingMessage } from "./useCodingSectionController";

type CodingMessageListProps = {
  messages: CodingMessage[];
  selectedModel: string;
  chatContainerRef: RefObject<HTMLDivElement | null>;
  isUserScrolledUp?: boolean;
  onScroll?: () => void;
  onScrollToBottom?: () => void;
  onApproveAgentToolCall: (callId: string, approved: boolean) => void;
  onOpenDiff: (file: string, code?: string, applied?: boolean) => void;
  onApplyCodeToFile: (file: string, code: string) => void;
  onDeleteWorkspaceFile: (file: string) => void;
  onOpenExternalFile: (file: string) => void;
};

export function CodingMessageList({
  messages,
  selectedModel,
  chatContainerRef,
  isUserScrolledUp = false,
  onScroll,
  onScrollToBottom,
  onApproveAgentToolCall,
  onOpenDiff,
  onApplyCodeToFile,
  onDeleteWorkspaceFile,
  onOpenExternalFile
}: CodingMessageListProps) {
  return (
    <div style={{ position: "relative", flex: "1 1 0", minHeight: 0, display: "flex", flexDirection: "column" }}>
      <div ref={chatContainerRef} className="vibe-chat-messages" onScroll={onScroll}>
        {messages.length === 0 ? (
          <div className="vibe-chat-empty-state">
            <div className="vibe-chat-empty-state__icon">
              <Bot size={48} style={{ color: "#818cf8" }} />
            </div>
            <h3 className="vibe-chat-empty-state__title">
              Coding &amp; Vibe Hub — Agente Autonomo
            </h3>
            <p className="vibe-chat-empty-state__text">
              Inserisci un obiettivo in linguaggio naturale. L'agente esplorerà il progetto, leggerà i file, applicherà le modifiche, eseguità i comandi di compilazione/test ed effettuerà l'auto-correzione in loop.
            </p>
          </div>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              className={`vibe-message-bubble ${
                msg.sender === "user"
                  ? "vibe-message-bubble--user"
                  : msg.agentEvents && msg.agentEvents.length > 0
                    ? "vibe-message-bubble--agent"
                    : "vibe-message-bubble--assistant"
              }`}
            >
              <div className="vibe-message-header">
                <span className="vibe-message-sender" style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
                  {msg.sender === "user" ? (
                    <><User size={14} /> Tu</>
                  ) : msg.agentEvents ? (
                    <><Bot size={14} style={{ color: "#4ade80" }} /> Agente ({selectedModel})</>
                  ) : (
                    <><Bot size={14} style={{ color: "#38bdf8" }} /> Assistant ({selectedModel})</>
                  )}
                </span>
                <span className="vibe-message-timestamp">{msg.timestamp}</span>
              </div>

              {msg.attachedFile && (
                <div className="vibe-message-attached-file" style={{ display: "flex", alignItems: "center", gap: 6 }}>
                  <FileCode size={14} /> File allegato: {msg.attachedFile}
                </div>
              )}

              {/* AGENT EVENTS RENDERING & REASONING TRACE */}
              {msg.sender === "assistant" || (msg.agentEvents && msg.agentEvents.length > 0) ? (
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  {msg.agentEvents && msg.agentEvents.length > 0 && (
                    <ReasoningTraceVisualizer
                      events={msg.agentEvents}
                      isStreaming={msg.isStreaming}
                    />
                  )}
                  {msg.agentEvents?.map((evt, idx) => (
                    <AgentToolCallCard
                      key={`${msg.id}_evt_${idx}`}
                      event={evt}
                      onApprove={(callId, approved) => onApproveAgentToolCall(callId, approved)}
                    />
                  ))}
                  {msg.isStreaming && (() => {
                    const events = msg.agentEvents || [];
                    const lastEvt = events.length > 0 ? events[events.length - 1] : null;

                    const stepMatch = events
                      .map((e) => (e.content || "").match(/\[Agent Step (\d+(?:\/\d+)?)\]/))
                      .filter(Boolean);
                    const currentStepLabel =
                      stepMatch.length > 0 ? stepMatch[stepMatch.length - 1]![1] : String(events.length || 1);

                    let statusText = `Passo ${currentStepLabel}: Caricamento modello ed elaborazione LLM in corso...`;
                    if (lastEvt) {
                      if (lastEvt.type === "tool_proposed" && lastEvt.toolCall) {
                        statusText = `Passo ${currentStepLabel}: Esecuzione strumento '${lastEvt.toolCall.toolName}'...`;
                      } else if (lastEvt.type === "tool_result" && lastEvt.toolResult) {
                        statusText = `Passo ${currentStepLabel}: Completato '${lastEvt.toolResult.toolName}'. Analisi del risultato...`;
                      } else if (lastEvt.type === "thought" || lastEvt.type === "thought_chunk") {
                        const snippet = (lastEvt.content || "").trim().slice(-140);
                        statusText = snippet
                          ? `Passo ${currentStepLabel}: ${snippet}`
                          : `Passo ${currentStepLabel}: Pensiero ed elaborazione in corso...`;
                      } else if (lastEvt.type === "approval_required" && lastEvt.toolCall) {
                        statusText = `Passo ${currentStepLabel}: Attesa approvazione utente per '${lastEvt.toolCall.toolName}'...`;
                      }
                    }
                    return (
                      <div className="vibe-agent-status-card">
                        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                          <Loader2 size={18} className="animate-spin text-indigo-400" style={{ flexShrink: 0 }} />
                          <span style={{ flex: 1, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                            {statusText}
                          </span>
                        </div>
                      </div>
                    );
                  })()}
                </div>
              ) : (
                <div className="vibe-message-content">
                  {msg.content}
                </div>
              )}

              {/* FILE ACTIONS & IMPACTED FILES */}
              {msg.fileActions && msg.fileActions.length > 0 && (
                <div className="file-actions-container">
                  <div className="file-actions-header" style={{ display: "flex", alignItems: "center", gap: 6 }}>
                    <FileCode size={14} /> Operazioni File Progetto:
                  </div>
                  <div>
                    {msg.fileActions.map((act) => (
                      <div key={act.file} className="file-action-card">
                        <span className={`file-action-label ${act.action === "delete" ? "file-action-label--delete" : "file-action-label--write"}`} style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
                          {act.action === "delete" ? <Trash2 size={13} /> : <FileCode size={13} />}
                          <span>{act.action.toUpperCase()}: {act.file}</span>
                          {act.applied && <span className="file-action-applied-badge" style={{ display: "inline-flex", alignItems: "center", gap: 4 }}><CheckCircle2 size={12} /> [Applicato]</span>}
                        </span>
                        <div className="file-action-buttons">
                          <button
                            type="button"
                            className="button button--secondary button--small"
                            style={{ fontSize: "0.75rem", display: "inline-flex", alignItems: "center", gap: 4 }}
                            onClick={() => onOpenDiff(act.file, act.code, act.applied)}
                            title="Visualizza il confronto modifiche file"
                          >
                            <FileDiff size={13} /> Diff
                          </button>
                          {!act.applied && act.action === "write" && act.code && (
                            <button
                              type="button"
                              className="button button--primary button--small"
                              style={{ fontSize: "0.75rem", display: "inline-flex", alignItems: "center", gap: 4 }}
                              onClick={() => onApplyCodeToFile(act.file, act.code!)}
                            >
                              <Save size={13} /> Salva su Disco
                            </button>
                          )}
                          {!act.applied && act.action === "delete" && (
                            <button
                              type="button"
                              className="button button--danger button--small"
                              style={{ fontSize: "0.75rem", display: "inline-flex", alignItems: "center", gap: 4 }}
                              onClick={() => onDeleteWorkspaceFile(act.file)}
                            >
                              <Trash2 size={13} /> Elimina
                            </button>
                          )}
                          <button
                            type="button"
                            className="button button--secondary button--small"
                            style={{ fontSize: "0.75rem", display: "inline-flex", alignItems: "center", gap: 4 }}
                            onClick={() => onOpenExternalFile(act.file)}
                          >
                            <ExternalLink size={13} /> Apri
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ))
        )}
      </div>

      {/* FLOATING ANTIGRAVITY SCROLL TO BOTTOM BUTTON */}
      {isUserScrolledUp && onScrollToBottom && (
        <button
          type="button"
          className="vibe-chat-scroll-bottom-btn"
          onClick={onScrollToBottom}
          title="Torna in fondo alla chat"
        >
          <ArrowDown size={15} /> Torna in basso
        </button>
      )}
    </div>
  );
}
