import {
  ArrowDown,
  CheckCircle2,
  CircleDotDashed,
  Clock3,
  Terminal,
  User,
  XCircle
} from "lucide-react";
import type { RefObject } from "react";
import { AgentToolCallCard } from "./AgentToolCallCard";
import { MarkdownRenderer } from "../common/MarkdownRenderer";
import type { CodingMessage } from "./useCodingSectionController";

type CodingMessageListProps = {
  messages: CodingMessage[];
  chatContainerRef: RefObject<HTMLDivElement | null>;
  isUserScrolledUp?: boolean;
  onScroll?: () => void;
  onScrollToBottom?: () => void;
  onApproveAgentToolCall: (callId: string, approved: boolean) => void;
};

function eventTitle(type: string) {
  switch (type) {
    case "state_changed":
      return "Stato agente";
    case "plan_update":
    case "plan_updated":
      return "Piano";
    case "tool_proposed":
      return "Strumento richiesto";
    case "tool_result":
      return "Risultato strumento";
    case "error":
      return "Errore";
    default:
      return "Attivita";
  }
}

export function CodingMessageList({
  messages,
  chatContainerRef,
  isUserScrolledUp = false,
  onScroll,
  onScrollToBottom,
  onApproveAgentToolCall
}: CodingMessageListProps) {
  return (
    <div className="coding-timeline-shell">
      <div ref={chatContainerRef} className="coding-timeline" onScroll={onScroll} aria-live="polite">
        {messages.length === 0 ? (
          <div className="coding-timeline__empty">
            <Terminal size={28} aria-hidden="true" />
            <h3>Inizia un task di coding</h3>
            <p>Descrivi il risultato atteso. L&apos;agente esplora, modifica e verifica solo attraverso gli strumenti autorizzati.</p>
          </div>
        ) : (
          messages.map((message) => (
            <article
              key={message.id}
              className={`coding-timeline-entry coding-timeline-entry--${message.sender}`}
            >
              <header className="coding-timeline-entry__header">
                <span className="coding-timeline-entry__author">
                  {message.sender === "user" ? <User size={14} aria-hidden="true" /> : <CircleDotDashed size={14} aria-hidden="true" />}
                  {message.sender === "user" ? "Tu" : "Agente"}
                </span>
                <time dateTime={message.timestamp}>
                  <Clock3 size={12} aria-hidden="true" />
                  {message.timestamp}
                </time>
              </header>

              {message.attachedFile && <p className="coding-timeline-entry__context">Contesto: {message.attachedFile}</p>}

              {message.sender === "user" && (
                <div className="coding-timeline-entry__content">
                  <MarkdownRenderer content={message.content} />
                </div>
              )}

              {message.sender === "assistant" && (
                <div className="coding-run-events">
                  {message.agentEvents?.filter((event) => event.type !== "thought" && event.type !== "thought_chunk" && event.type !== "final_response").map((event, index) => {
                    const isResult = event.type === "tool_result" && event.toolResult;
                    const isFailure = event.type === "error" || (isResult && !event.toolResult?.success);
                    const icon = isFailure
                      ? <XCircle size={15} aria-hidden="true" />
                      : isResult && event.toolResult?.success
                        ? <CheckCircle2 size={15} aria-hidden="true" />
                        : <CircleDotDashed size={15} aria-hidden="true" />;
                    const detail = event.planMarkdown ?? event.content ?? event.toolResult?.output ?? event.toolResult?.error;

                    if (event.type === "approval_required") {
                      return (
                        <AgentToolCallCard
                          key={`${message.id}-${event.toolCall?.callId ?? index}`}
                          event={event}
                          onApprove={onApproveAgentToolCall}
                        />
                      );
                    }

                    return (
                      <details
                        key={`${message.id}-${event.type}-${event.toolCall?.callId ?? event.toolResult?.callId ?? index}`}
                        className={`coding-run-event ${isFailure ? "coding-run-event--failure" : ""}`}
                      >
                        <summary>
                          {icon}
                          <span>{eventTitle(event.type)}</span>
                          {(event.toolCall?.toolName ?? event.toolResult?.toolName) && (
                            <code>{event.toolCall?.toolName ?? event.toolResult?.toolName}</code>
                          )}
                        </summary>
                        {detail && <pre>{detail}</pre>}
                        {event.toolResult?.diffPatch && <pre className="coding-run-event__diff">{event.toolResult.diffPatch}</pre>}
                      </details>
                    );
                  })}
                  {message.isStreaming && (
                    <div className="coding-run-event coding-run-event--running">
                      <CircleDotDashed size={15} aria-hidden="true" />
                      <span>In esecuzione</span>
                    </div>
                  )}
                  {message.content && (
                    <div className="coding-timeline-entry__content coding-timeline-entry__content--final">
                      <MarkdownRenderer content={message.content} />
                    </div>
                  )}
                </div>
              )}
            </article>
          ))
        )}
      </div>
      {isUserScrolledUp && onScrollToBottom && (
        <button type="button" className="coding-timeline__scroll-bottom" onClick={onScrollToBottom}>
          <ArrowDown size={15} aria-hidden="true" />
          Vai al fondo
        </button>
      )}
    </div>
  );
}
