import { useEffect, useRef, useState, type FormEvent, type KeyboardEvent } from "react";
import type { ChatSource, ImportedDocument, OllamaModel, OllamaStatusResponse } from "../../api";
import { formatPageRange } from "./ChatSection.helpers";
import { exportSessionAsJson, exportSessionAsMarkdown, triggerFileDownload, type ChatMessage } from "./ChatSection.storage";
import { MarkdownRenderer } from "../common/MarkdownRenderer";
import { ModelSelectorDropdown } from "../common/ModelSelectorDropdown";

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
  const [docSearchQuery, setDocSearchQuery] = useState("");

  const filteredDocuments = documents.filter((doc) =>
    doc.originalFileName.toLowerCase().includes(docSearchQuery.toLowerCase().trim())
  );

  return (
    <section className="chat-documents-panel" aria-labelledby="chat-documents-title">
      <div className="settings-card__header">
        <h2 id="chat-documents-title">Documenti</h2>
      </div>

      {documentsError && <div className="feedback-banner feedback-banner--error" role="alert">{documentsError}</div>}

      <div className="px-2 pb-2">
        <input
          type="text"
          placeholder="Cerca documenti..."
          value={docSearchQuery}
          onChange={(e) => setDocSearchQuery(e.target.value)}
          className="w-full px-2 py-1 bg-card border border-light rounded text-xs text-main focus:outline-none focus:border-focus"
        />
      </div>

      <div className="chat-document-list" aria-label="Documenti selezionabili per la chat">
        {isDocsLoading ? (
          <div className="empty-state chat-empty-state" role="status" aria-live="polite">
            <p>Caricamento...</p>
          </div>
        ) : filteredDocuments.length === 0 ? (
          <div className="empty-state chat-empty-state" role="status">
            <p>{docSearchQuery ? "Nessun documento corrisponde." : "Nessun documento importato."}</p>
          </div>
        ) : (
          filteredDocuments.map((document) => (
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

  function handleExportMarkdown() {
    if (messages.length === 0) return;
    const markdown = exportSessionAsMarkdown(messages, null, selectedModel);
    triggerFileDownload(markdown, `chat-export-${Date.now()}.md`, "text/markdown");
  }

  function handleExportJson() {
    if (messages.length === 0) return;
    const json = exportSessionAsJson(messages, null, selectedModel);
    triggerFileDownload(json, `chat-export-${Date.now()}.json`, "application/json");
  }

  const textareaRef = useRef<HTMLTextAreaElement | null>(null);

  useEffect(() => {
    if (textareaRef.current) {
      textareaRef.current.style.height = "auto";
      const newHeight = Math.min(200, Math.max(48, textareaRef.current.scrollHeight));
      textareaRef.current.style.height = `${newHeight}px`;
    }
  }, [input]);

  const SYSTEM_PROMPT_TEMPLATES = [
    { label: "📊 Analisi Critica", text: "Esegui un'analisi critica approfondita ed obiettiva mettendo in luce pro, contro e punti chiave:\n\n" },
    { label: "📋 Sintesi Esecutiva", text: "Fornisci una sintesi esecutiva strutturata con punti elenco e raccomandazioni pratiche:\n\n" },
    { label: "⚙️ Estrazione JSON", text: "Estrai le informazioni rilevanti nel seguente formato JSON strutturato e valido:\n\n" },
    { label: "💡 Spiegazione Semplice", text: "Spiega il concetto in modo semplice e chiaro, con metafore ed esempi pratici:\n\n" },
    { label: "🔍 Code Reviewer", text: "Effettua una code review dettagliata individuando bug, problemi di performance e refactoring:\n\n" }
  ];

  return (
    <section className="chat-main" aria-label="Chat RAG">
      <div className="chat-toolbar">
        <div className="flex items-center gap-2">
          <label className="text-xs font-semibold text-muted sr-only" htmlFor="chat-model">Modello Chat</label>
          {/* Accessible hidden select for Playwright/Vitest backwards compatibility */}
          <select
            id="chat-model"
            aria-label="Modello chat"
            className="sr-only"
            value={selectedModel}
            onChange={(event) => onSelectedModelChange(event.target.value)}
          >
            <option value="">Seleziona modello</option>
            {models.map((m) => (
              <option key={m.name} value={m.name}>{m.name}</option>
            ))}
          </select>

          <ModelSelectorDropdown
            models={models}
            selectedModel={selectedModel}
            onSelectModel={onSelectedModelChange}
            disabled={!ollamaStatus?.isReachable || models.length === 0 || isGenerating}
          />
        </div>
        <div className="settings-actions flex items-center gap-2">
          {messages.length > 0 && (
            <>
              <button
                className="button-secondary button-secondary--xs"
                type="button"
                onClick={handleExportMarkdown}
                title="Esporta conversazione in formato Markdown (.md)"
              >
                Esporta MD
              </button>
              <button
                className="button-secondary button-secondary--xs"
                type="button"
                onClick={handleExportJson}
                title="Esporta conversazione in formato JSON (.json)"
              >
                Esporta JSON
              </button>
            </>
          )}
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
          <div className="vibe-chat-empty-state" role="status">
            <div className="vibe-chat-empty-state__icon">✨</div>
            <h3 className="vibe-chat-empty-state__title">Inizia una conversazione.</h3>
            <p className="vibe-chat-empty-state__text">
              Poni domande sui tuoi documenti locali, analizza codice o genera contenuti con i modelli RAG 2.0.
            </p>
            <div className="preset-buttons-row" style={{ marginTop: "16px", justifyContent: "center" }}>
              <button
                type="button"
                className="button-secondary"
                onClick={() => onInputChange("Fai un riassunto dei concetti chiave nei documenti selezionati.")}
              >
                📄 Riassumi documenti RAG
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={() => onInputChange("Spiega come funziona l'architettura RAG 2.0 e il re-ranking ONNX.")}
              >
                🧠 Spiega architettura RAG
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={() => onInputChange("Estrai le entità principali e la sintesi dai file importati.")}
              >
                🔍 Estrai entità e sintesi
              </button>
            </div>
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

      <div className="chat-prompt-templates" style={{ display: "flex", gap: "6px", overflowX: "auto", padding: "4px 8px", background: "rgba(15, 23, 42, 0.6)", borderRadius: "6px 6px 0 0", borderTop: "1px solid #1e293b" }}>
        <span style={{ fontSize: "0.75rem", color: "#64748b", fontWeight: 600, display: "flex", alignItems: "center", whiteSpace: "nowrap" }}>Template:</span>
        {SYSTEM_PROMPT_TEMPLATES.map((tpl) => (
          <button
            key={tpl.label}
            type="button"
            className="button-secondary button-secondary--xs"
            style={{ fontSize: "0.74rem", padding: "2px 8px", whiteSpace: "nowrap" }}
            onClick={() => onInputChange(input ? `${tpl.text}${input}` : tpl.text)}
            title="Inserisci questo template di system prompt"
          >
            {tpl.label}
          </button>
        ))}
      </div>

      <form className="chat-input-row" onSubmit={handleFormSubmit}>
        <textarea
          ref={textareaRef}
          aria-label="Messaggio"
          value={input}
          rows={1}
          style={{ minHeight: "48px", maxHeight: "200px", resize: "none", overflowY: "auto" }}
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
