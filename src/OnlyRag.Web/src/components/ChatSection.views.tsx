import { useEffect, useRef, type FormEvent, type KeyboardEvent } from "react";
import type { ChatSource, ImportedDocument, OllamaModel, OllamaStatusResponse } from "../api";
import { formatPageRange } from "./ChatSection.helpers";
import type { ChatMessage } from "./ChatSection.storage";
import { MarkdownRenderer } from "./MarkdownRenderer";

export function ChatDocumentsPanel({
  documentsError,
  isDocsLoading,
  documents,
  selectedDocumentIds,
  onToggleDocument
}: {
  documentsError: string | null;
  isDocsLoading: boolean;
  documents: ImportedDocument[];
  selectedDocumentIds: number[];
  onToggleDocument: (documentId: number, checked: boolean) => void;
}) {
  return (
    <section className="chat-documents-panel" aria-labelledby="chat-documents-title">
      <div className="settings-card__header">
        <h2 id="chat-documents-title">Documenti</h2>
      </div>

      {documentsError && <div className="feedback-banner feedback-banner--error" role="alert">{documentsError}</div>}

      <div className="chat-document-list" aria-label="Documenti selezionabili per la chat">
        {isDocsLoading ? (
          <div className="empty-state chat-empty-state" role="status" aria-live="polite">
            <p>Caricamento...</p>
          </div>
        ) : documents.length === 0 ? (
          <div className="empty-state chat-empty-state" role="status">
            <p>Nessun documento importato.</p>
          </div>
        ) : (
          documents.map((document) => (
            <label className="chat-document-row" key={document.id} title={document.originalFileName}>
              <input
                type="checkbox"
                checked={selectedDocumentIds.includes(document.id)}
                onChange={(event) => onToggleDocument(document.id, event.target.checked)}
              />
              <span className="chat-document-row__text">
                <strong>{document.originalFileName}</strong>
                <span className={`status-badge status-badge--${document.chunkCount > 0 ? "online" : "warning"}`}>
                  {document.chunkCount > 0 ? "Pronto" : "Elaborazione"}
                </span>
              </span>
            </label>
          ))
        )}
      </div>
    </section>
  );
}

export function ChatResizeHandle({
  minDocumentsPanelWidth,
  maxDocumentsPanelWidth,
  documentsPanelWidth,
  onPointerDown,
  onKeyDown
}: {
  minDocumentsPanelWidth: number;
  maxDocumentsPanelWidth: number;
  documentsPanelWidth: number;
  onPointerDown: () => void;
  onKeyDown: (event: KeyboardEvent<HTMLDivElement>) => void;
}) {
  return (
    <div
      className="chat-resize-handle"
      role="separator"
      aria-label="Ridimensiona pannelli chat"
      aria-orientation="vertical"
      aria-valuemin={minDocumentsPanelWidth}
      aria-valuemax={maxDocumentsPanelWidth}
      aria-valuenow={Math.round(documentsPanelWidth)}
      tabIndex={0}
      onPointerDown={(event) => {
        event.preventDefault();
        onPointerDown();
      }}
      onKeyDown={onKeyDown}
    />
  );
}

export function ChatMainPanel({
  models,
  selectedModel,
  ollamaStatus,
  loadError,
  isGenerating,
  feedback,
  notices,
  messages,
  input,
  selectedDocumentIds,
  canSend,
  onSelectedModelChange,
  onNewChat,
  onCancel,
  onInputChange,
  onInputKeyDown,
  onOpenSource,
  onSubmit
}: {
  models: OllamaModel[];
  selectedModel: string;
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
  isGenerating: boolean;
  feedback: string | null;
  notices: string[];
  messages: ChatMessage[];
  input: string;
  selectedDocumentIds: number[];
  canSend: boolean;
  onSelectedModelChange: (value: string) => void;
  onNewChat: () => void;
  onCancel: () => void;
  onInputChange: (value: string) => void;
  onInputKeyDown: (event: KeyboardEvent<HTMLTextAreaElement>) => void;
  onOpenSource: (source: ChatSource) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const messagesRef = useRef<HTMLDivElement | null>(null);
  const messagesEndRef = useRef<HTMLDivElement | null>(null);
  const shouldStickToBottomRef = useRef(true);

  useEffect(() => {
    if (!shouldStickToBottomRef.current) {
      return;
    }

    const scrollToBottom = () => {
      if (typeof messagesEndRef.current?.scrollIntoView === "function") {
        messagesEndRef.current.scrollIntoView({ behavior: "smooth", block: "end" });
      } else if (messagesRef.current) {
        messagesRef.current.scrollTop = messagesRef.current.scrollHeight;
      }
    };

    scrollToBottom();
    const timer = setTimeout(scrollToBottom, 60);
    return () => clearTimeout(timer);
  }, [messages, isGenerating, feedback, notices]);

  function handleMessagesScroll() {
    const element = messagesRef.current;
    if (!element) {
      return;
    }

    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    shouldStickToBottomRef.current = distanceFromBottom < 80;
  }

  function handleFormSubmit(event: FormEvent<HTMLFormElement>) {
    shouldStickToBottomRef.current = true;
    onSubmit(event);
  }

  return (
    <section className="chat-main" aria-label="Chat RAG">
      <div className="chat-toolbar">
        <label className="field-group chat-model-field" htmlFor="chat-model">
          <span>Modello chat</span>
          <select
            id="chat-model"
            value={selectedModel}
            onChange={(event) => onSelectedModelChange(event.target.value)}
            disabled={!ollamaStatus?.isReachable || models.length === 0 || isGenerating}
          >
            <option value="">Seleziona un modello disponibile</option>
            {models.map((model) => (
              <option key={model.name} value={model.name}>
                {model.name}
              </option>
            ))}
          </select>
        </label>
        <div className="settings-actions">
          <button className="button-secondary" type="button" onClick={onNewChat}>
            Nuova chat
          </button>
        </div>
      </div>

      <div className="chat-status-stack">
        {!ollamaStatus?.isReachable && (
          <div className="feedback-banner feedback-banner--error" role="alert">
            {loadError ?? "Ollama è offline. Apri Ollama o correggi l'indirizzo in Impostazioni."}
          </div>
        )}
        {ollamaStatus?.isReachable && models.length === 0 && (
          <div className="feedback-banner feedback-banner--error" role="alert">
            Nessun modello installato in Ollama. Installa almeno un modello prima di usare la chat.
          </div>
        )}
        {feedback && <div className="feedback-banner feedback-banner--error" role="alert">{feedback}</div>}
        {notices.length > 0 && (
          <div className="panel-note panel-note--warning" role="status">
            {notices.map((notice) => (
              <p key={notice}>{notice}</p>
            ))}
          </div>
        )}
      </div>

      <div className="chat-messages" aria-live="polite" ref={messagesRef} onScroll={handleMessagesScroll}>
        {messages.length === 0 ? (
          <div className="empty-state chat-empty-state" role="status">
            <p>Inizia una conversazione.</p>
          </div>
        ) : (
          messages.map((message) => (
            <article className={`chat-message chat-message--${message.role}`} key={message.id}>
              <div className="chat-message__bubble">
                <MarkdownRenderer content={message.content} />
                {message.sources.length > 0 && (
                  <div className="chat-sources">
                    <strong>Fonti RAG</strong>
                    {message.sources.map((source) => (
                      <details key={source.chunkId} className="chat-source">
                        <summary>
                          {source.documentName} - {formatPageRange(source.pageStart, source.pageEnd)}
                          {source.score !== undefined && (
                            <span className="status-badge status-badge--online chat-source__score">
                              Score: {source.score}
                            </span>
                          )}
                        </summary>
                        <button
                          className="button-secondary chat-source__open"
                          type="button"
                          onClick={() => onOpenSource(source)}
                        >
                          Apri pagina
                        </button>
                        <p><strong>Estratto:</strong> {source.snippet}</p>
                      </details>
                    ))}
                  </div>
                )}
              </div>
            </article>
          ))
        )}
        {isGenerating && (
          <div className="chat-generating" role="status" aria-live="polite">
            <span>Generazione in corso...</span>
            <button className="button-secondary" type="button" onClick={onCancel}>
              Annulla
            </button>
          </div>
        )}
        <div ref={messagesEndRef} />
      </div>

      <form className="chat-input-row" onSubmit={handleFormSubmit}>
        <textarea
          aria-label="Messaggio"
          value={input}
          rows={3}
          onChange={(event) => onInputChange(event.target.value)}
          onKeyDown={onInputKeyDown}
          placeholder={selectedDocumentIds.length > 0 ? "Fai una domanda sui documenti selezionati" : "Scrivi un messaggio"}
          disabled={isGenerating}
        />
        <button type="submit" disabled={!canSend || !input.trim()}>
          Invia
        </button>
      </form>
    </section>
  );
}
