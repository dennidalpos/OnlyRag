import { useEffect, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from "react";
import {
  apiRequest,
  type ChatResponse,
  type ChatSource,
  type ImportedDocument,
  type OllamaModel,
  type OllamaStatusResponse
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";

type ChatSectionProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
};

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources: ChatSource[];
};

type PersistedChatSession = {
  conversationId: string | null;
  messages: ChatMessage[];
  selectedDocumentIds: number[];
};

function loadChatSession(): PersistedChatSession | null {
  try {
    const raw = window.sessionStorage.getItem("onlyrag.chat.session");
    if (!raw) return null;
    return JSON.parse(raw) as PersistedChatSession;
  } catch {
    return null;
  }
}

function saveChatSession(session: PersistedChatSession): void {
  try {
    window.sessionStorage.setItem("onlyrag.chat.session", JSON.stringify(session));
  } catch {
  }
}

function clearChatSession(): void {
  try {
    window.sessionStorage.removeItem("onlyrag.chat.session");
  } catch {
  }
}

export function ChatSection({
  models,
  defaultModel,
  ollamaStatus,
  loadError
}: ChatSectionProps) {
  const draftStorageKey = "onlyrag.chat.draft";
  const documentsPanelWidthStorageKey = "onlyrag.chat.documentsPanelWidth";
  const minDocumentsPanelWidth = 180;
  const maxDocumentsPanelWidth = 420;
  const minChatPanelWidth = 360;
  const abortControllerRef = useRef<AbortController | null>(null);
  const chatLayoutRef = useRef<HTMLDivElement | null>(null);
  const [selectedModel, setSelectedModel] = useState("");
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);

  const [selectedDocumentIds, setSelectedDocumentIds] = useState<number[]>(() => loadChatSession()?.selectedDocumentIds ?? []);
  const [conversationId, setConversationId] = useState<string | null>(() => loadChatSession()?.conversationId ?? null);
  const [messages, setMessages] = useState<ChatMessage[]>(() => loadChatSession()?.messages ?? []);
  const [input, setInput] = useState("");
  const [documentsPanelWidth, setDocumentsPanelWidth] = useState(() => loadDocumentsPanelWidth(documentsPanelWidthStorageKey, minDocumentsPanelWidth, maxDocumentsPanelWidth));
  const [isResizingDocumentsPanel, setIsResizingDocumentsPanel] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isDocsLoading, setIsDocsLoading] = useState(true);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [notices, setNotices] = useState<string[]>([]);

  useEffect(() => {
    try {
      const savedDraft = window.localStorage.getItem(draftStorageKey);
      if (savedDraft) {
        setInput(savedDraft);
      }
    } catch {
    }
  }, []);

  useEffect(() => {
    try {
      if (input.trim().length === 0) {
        window.localStorage.removeItem(draftStorageKey);
      } else {
        window.localStorage.setItem(draftStorageKey, input);
      }
    } catch {
    }
  }, [input]);

  useEffect(() => {
    saveChatSession({ conversationId, messages, selectedDocumentIds });
  }, [conversationId, messages, selectedDocumentIds]);

  useEffect(() => {
    try {
      window.localStorage.setItem(documentsPanelWidthStorageKey, String(documentsPanelWidth));
    } catch {
    }
  }, [documentsPanelWidth]);

  useEffect(() => {
    if (!isResizingDocumentsPanel) {
      return;
    }

    function handlePointerMove(event: PointerEvent) {
      const layout = chatLayoutRef.current;
      if (!layout) {
        return;
      }

      const layoutRect = layout.getBoundingClientRect();
      const maxAllowedWidth = getMaxDocumentsPanelWidth(
        layout,
        minDocumentsPanelWidth,
        maxDocumentsPanelWidth,
        minChatPanelWidth
      );
      const nextWidth = clamp(
        event.clientX - layoutRect.left,
        minDocumentsPanelWidth,
        maxAllowedWidth
      );
      setDocumentsPanelWidth(nextWidth);
    }

    function handlePointerUp() {
      setIsResizingDocumentsPanel(false);
    }

    document.body.classList.add("is-chat-resizing");
    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", handlePointerUp);

    return () => {
      document.body.classList.remove("is-chat-resizing");
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", handlePointerUp);
    };
  }, [isResizingDocumentsPanel]);

  useEffect(() => {
    const modelNames = models.map((model) => model.name);
    const nextValue =
      defaultModel && modelNames.includes(defaultModel) ? defaultModel : modelNames[0] ?? "";
    setSelectedModel(nextValue);
  }, [defaultModel, models]);

  useEffect(() => {
    let isCancelled = false;

    async function loadDocuments() {
      try {
        const docs = await apiRequest<ImportedDocument[]>("/api/documents");
        if (isCancelled) {
          return;
        }

        setDocuments(docs);
        setSelectedDocumentIds((current) => {
          const available = new Set(docs.map((document) => document.id));
          return current.filter((id) => available.has(id));
        });
        setDocumentsError(null);
      } catch (error) {
        if (!isCancelled) {
          setDocumentsError(error instanceof Error ? error.message : "Documenti non disponibili.");
        }
      } finally {
        if (!isCancelled) {
          setIsDocsLoading(false);
        }
      }
    }

    void loadDocuments();

    return () => {
      isCancelled = true;
    };
  }, []);

  useEffect(() => {
    setExitContributor("chat", {
      label: "Chat",
      hasPendingChanges: input.trim().length > 0,
      hasActiveWork: isGenerating,
      prepareForExit: async () => {
        abortControllerRef.current?.abort();
        try {
          if (input.trim().length === 0) {
            window.localStorage.removeItem(draftStorageKey);
          } else {
            window.localStorage.setItem(draftStorageKey, input);
          }
        } catch {
        }
      }
    });

    return () => {
      clearExitContributor("chat");
    };
  }, [input, isGenerating]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const message = input.trim();
    if (!message) {
      return;
    }

    if (!selectedModel) {
      setFeedback("Seleziona un modello Ollama installato prima di inviare.");
      return;
    }


    const userMessage: ChatMessage = {
      id: crypto.randomUUID(),
      role: "user",
      content: message,
      sources: []
    };
    setMessages((current) => [...current, userMessage]);
    setInput("");
    setFeedback(null);
    setNotices([]);
    setIsGenerating(true);

    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    try {
      const response = await apiRequest<ChatResponse>("/api/chat", {
        method: "POST",
        signal: abortController.signal,
        body: JSON.stringify({
          message,
          model: selectedModel,
          useDocuments: selectedDocumentIds.length > 0,
          selectedDocumentIds,
          conversationId
        })
      });

      setConversationId(response.conversationId);
      setMessages((current) => [
        ...current,
        {
          id: crypto.randomUUID(),
          role: "assistant",
          content: response.answer,
          sources: response.sources
        }
      ]);
      setNotices(response.notices.map((notice) => notice.message));
      try {
        window.localStorage.removeItem(draftStorageKey);
      } catch {
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        setFeedback("Generazione annullata.");
      } else {
        setFeedback(error instanceof Error ? error.message : "Generazione non riuscita.");
      }
    } finally {
      setIsGenerating(false);
      abortControllerRef.current = null;
    }
  }

  function handleCancel() {
    abortControllerRef.current?.abort();
  }

  function handleInputKeyDown(event: KeyboardEvent<HTMLTextAreaElement>) {
    if (
      event.key !== "Enter" ||
      event.shiftKey ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      event.nativeEvent.isComposing
    ) {
      return;
    }

    event.preventDefault();
    if (!canSend || input.trim().length === 0) {
      return;
    }

    event.currentTarget.form?.requestSubmit();
  }

  function handleResizeHandleKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
      return;
    }

    event.preventDefault();
    const maxAllowedWidth = getMaxDocumentsPanelWidth(
      chatLayoutRef.current,
      minDocumentsPanelWidth,
      maxDocumentsPanelWidth,
      minChatPanelWidth
    );
    setDocumentsPanelWidth((current) => {
      if (event.key === "Home") {
        return minDocumentsPanelWidth;
      }

      if (event.key === "End") {
        return maxAllowedWidth;
      }

      const delta = event.key === "ArrowLeft" ? -24 : 24;
      return clamp(current + delta, minDocumentsPanelWidth, maxAllowedWidth);
    });
  }

  function toggleDocument(documentId: number, checked: boolean) {
    setSelectedDocumentIds((current) =>
      checked
        ? current.includes(documentId) ? current : [...current, documentId]
        : current.filter((id) => id !== documentId)
    );
  }

  function handleNewChat() {
    abortControllerRef.current?.abort();
    setConversationId(null);
    setMessages([]);
    setInput("");
    setFeedback(null);
    setNotices([]);
    clearChatSession();
    try {
      window.localStorage.removeItem(draftStorageKey);
    } catch {
    }
  }

  const canSend = Boolean(ollamaStatus?.isReachable && models.length > 0 && selectedModel && !isGenerating);

  return (
    <div
      className={isResizingDocumentsPanel ? "chat-layout chat-layout--resizing" : "chat-layout"}
      ref={chatLayoutRef}
      style={{ "--chat-documents-width": `${documentsPanelWidth}px` } as CSSProperties}
    >
      <aside className="chat-documents-panel">
        <div className="settings-card__header">
          <h3>Documenti</h3>
        </div>

        {documentsError && <div className="feedback-banner feedback-banner--error" role="alert">{documentsError}</div>}

        <div className="chat-document-list" aria-label="Documenti selezionabili per la chat">
          {isDocsLoading ? (
            <div className="empty-state chat-empty-state">
              <p>Caricamento...</p>
            </div>
          ) : documents.length === 0 ? (
            <div className="empty-state chat-empty-state">
              <p>Nessun documento importato.</p>
            </div>
          ) : (
            documents.map((document) => (
              <label className="chat-document-row" key={document.id}>
                <input
                  type="checkbox"
                  checked={selectedDocumentIds.includes(document.id)}
                  onChange={(event) => toggleDocument(document.id, event.target.checked)}
                />
                <span>
                  <strong>{document.originalFileName}</strong>
                  <small>
                    {document.chunkCount === 0 ? "Non ancora pronto per la ricerca" : "Pronto per la ricerca"}
                  </small>
                </span>
              </label>
            ))
          )}
        </div>
      </aside>

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
          setIsResizingDocumentsPanel(true);
        }}
        onKeyDown={handleResizeHandleKeyDown}
      />

      <section className="chat-main" aria-label="Chat RAG">
        <div className="chat-toolbar">
          <label className="field-group chat-model-field" htmlFor="chat-model">
            <span>Modello chat</span>
            <select
              id="chat-model"
              value={selectedModel}
              onChange={(event) => setSelectedModel(event.target.value)}
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
            <button className="button-secondary" type="button" onClick={handleNewChat}>
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

        <div className="chat-messages" aria-live="polite">
          {messages.length === 0 ? (
            <div className="empty-state chat-empty-state">
              <p>Inizia una conversazione.</p>
            </div>
          ) : (
            messages.map((message) => (
              <article className={`chat-message chat-message--${message.role}`} key={message.id}>
                <div className="chat-message__bubble">
                  <p>{message.content}</p>
                  {message.sources.length > 0 && (
                    <div className="chat-sources">
                      <strong>Fonti</strong>
                      {message.sources.map((source) => (
                        <details key={source.chunkId} className="chat-source">
                          <summary>
                            {source.documentName} - {formatPageRange(source.pageStart, source.pageEnd)}
                          </summary>
                          <p>{source.snippet}</p>
                        </details>
                      ))}
                    </div>
                  )}
                </div>
              </article>
            ))
          )}
          {isGenerating && (
            <div className="chat-generating">
              <span>Generazione in corso...</span>
              <button className="button-secondary" type="button" onClick={handleCancel}>
                Annulla
              </button>
            </div>
          )}
        </div>

        <form className="chat-input-row" onSubmit={handleSubmit}>
          <textarea
            aria-label="Messaggio"
            value={input}
            rows={3}
            onChange={(event) => setInput(event.target.value)}
            onKeyDown={handleInputKeyDown}
            placeholder={selectedDocumentIds.length > 0 ? "Fai una domanda sui documenti selezionati" : "Scrivi un messaggio"}
            disabled={isGenerating}
          />
          <button type="submit" disabled={!canSend || !input.trim()}>
            Invia
          </button>
        </form>
      </section>
    </div>
  );
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function loadDocumentsPanelWidth(storageKey: string, min: number, max: number): number {
  try {
    const saved = Number(window.localStorage.getItem(storageKey));
    return Number.isFinite(saved) ? clamp(saved, min, max) : 220;
  } catch {
    return 220;
  }
}

function getMaxDocumentsPanelWidth(
  layout: HTMLDivElement | null,
  minDocumentsPanelWidth: number,
  maxDocumentsPanelWidth: number,
  minChatPanelWidth: number
): number {
  if (!layout) {
    return maxDocumentsPanelWidth;
  }

  return Math.max(
    minDocumentsPanelWidth,
    Math.min(maxDocumentsPanelWidth, layout.getBoundingClientRect().width - minChatPanelWidth)
  );
}

function formatPageRange(pageStart: number | null, pageEnd: number | null): string {
  if (!pageStart && !pageEnd) {
    return "Pagina non disponibile";
  }

  if (!pageEnd || pageStart === pageEnd) {
    return `Pagina ${pageStart}`;
  }

  return `Pagine ${pageStart}-${pageEnd}`;
}
