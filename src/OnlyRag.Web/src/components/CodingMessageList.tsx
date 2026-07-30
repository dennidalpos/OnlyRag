import type { RefObject } from "react";
import { AgentToolCallCard } from "./AgentToolCallCard";
import { ReasoningTraceVisualizer } from "./ReasoningTraceVisualizer";
import type { CodingMessage } from "./useCodingSectionController";

type CodingMessageListProps = {
  messages: CodingMessage[];
  selectedModel: string;
  chatContainerRef: RefObject<HTMLDivElement | null>;
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
  onApproveAgentToolCall,
  onOpenDiff,
  onApplyCodeToFile,
  onDeleteWorkspaceFile,
  onOpenExternalFile
}: CodingMessageListProps) {
  return (
    <div ref={chatContainerRef} className="vibe-chat-messages">
      {messages.length === 0 ? (
        <div className="vibe-chat-empty-state">
          <div className="vibe-chat-empty-state__icon">🤖</div>
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
              <span className="vibe-message-sender">
                {msg.sender === "user"
                  ? "👤 Tu"
                  : msg.agentEvents
                    ? `🤖 Agente (${selectedModel})`
                    : `🤖 Assistant (${selectedModel})`}
              </span>
              <span className="vibe-message-timestamp">{msg.timestamp}</span>
            </div>

            {msg.attachedFile && (
              <div className="vibe-message-attached-file">
                📄 File allegato: {msg.attachedFile}
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

                  let statusText = `⚡ Passo ${currentStepLabel}: Caricamento modello ed elaborazione LLM in corso...`;
                  if (lastEvt) {
                    if (lastEvt.type === "tool_proposed" && lastEvt.toolCall) {
                      statusText = `🛠️ Passo ${currentStepLabel}: Esecuzione strumento '${lastEvt.toolCall.toolName}'...`;
                    } else if (lastEvt.type === "tool_result" && lastEvt.toolResult) {
                      statusText = `✅ Passo ${currentStepLabel}: Completato '${lastEvt.toolResult.toolName}'. Analisi del risultato...`;
                    } else if (lastEvt.type === "thought" || lastEvt.type === "thought_chunk") {
                      const snippet = (lastEvt.content || "").trim().slice(-140);
                      statusText = snippet
                        ? `⚡ Passo ${currentStepLabel}: ${snippet}`
                        : `⚡ Passo ${currentStepLabel}: Pensiero ed elaborazione in corso...`;
                    } else if (lastEvt.type === "approval_required" && lastEvt.toolCall) {
                      statusText = `⚠️ Passo ${currentStepLabel}: Attesa approvazione utente per '${lastEvt.toolCall.toolName}'...`;
                    }
                  }
                  return (
                    <div className="vibe-agent-status-card">
                      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                        <span style={{ fontSize: "1.1rem", animation: "spin 1.5s linear infinite" }}>⏳</span>
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
                <div className="file-actions-header">
                  🛠️ Operazioni File Progetto:
                </div>
                <div>
                  {msg.fileActions.map((act) => (
                    <div key={act.file} className="file-action-card">
                      <span className={`file-action-label ${act.action === "delete" ? "file-action-label--delete" : "file-action-label--write"}`}>
                        {act.action === "delete" ? "🗑️ DELETE" : "📝 WRITE"}: {act.file}
                        {act.applied && <span className="file-action-applied-badge">[Applicato su Disco]</span>}
                      </span>
                      <div className="file-action-buttons">
                        <button
                          type="button"
                          className="button button--secondary button--small"
                          style={{ fontSize: "0.75rem" }}
                          onClick={() => onOpenDiff(act.file, act.code, act.applied)}
                          title="Visualizza il confronto modifiche file"
                        >
                          🔍 Diff
                        </button>
                        {!act.applied && act.action === "write" && act.code && (
                          <button
                            type="button"
                            className="button button--primary button--small"
                            style={{ fontSize: "0.75rem" }}
                            onClick={() => onApplyCodeToFile(act.file, act.code!)}
                          >
                            💾 Salva su Disco
                          </button>
                        )}
                        {!act.applied && act.action === "delete" && (
                          <button
                            type="button"
                            className="button button--danger button--small"
                            style={{ fontSize: "0.75rem" }}
                            onClick={() => onDeleteWorkspaceFile(act.file)}
                          >
                            🗑️ Elimina
                          </button>
                        )}
                        <button
                          type="button"
                          className="button button--secondary button--small"
                          style={{ fontSize: "0.75rem" }}
                          onClick={() => onOpenExternalFile(act.file)}
                        >
                          🔗 Apri
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
  );
}
