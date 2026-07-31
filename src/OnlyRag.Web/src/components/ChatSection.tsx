import { useCallback, useEffect, useRef, useState, type CSSProperties, type FormEvent, type KeyboardEvent } from "react";
import {
  apiRequest,
  apiStreamRequest,
  type ChatSource,
  type ChatStreamChunkEvent,
  type ImportedDocument,
  type OllamaModel,
  type OllamaStatusResponse
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import { clampNumber } from "../numberUtils";
import { getMaxDocumentsPanelWidth } from "./ChatSection.helpers";
import {
  clearChatDraft,
  clearChatSession,
  loadChatDraft,
  loadChatSession,
  loadDocumentsPanelWidth,
  saveChatSession,
  saveDocumentsPanelWidth,
  saveOrClearChatDraft,
  type ChatMessage
} from "./ChatSection.storage";
import { ChatDocumentsPanel, ChatMainPanel, ChatResizeHandle } from "./ChatSection.views";
import { DocumentPreviewModal } from "./DocumentPreviewModal";
import { useDocumentPreviewController } from "./useDocumentPreviewController";

type ChatSectionProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
  documentLibraryVersion?: number;
  isActive?: boolean;
};

export function ChatSection({
  models,
  defaultModel,
  ollamaStatus,
  loadError,
  documentLibraryVersion = 0,
  isActive = true
}: ChatSectionProps) {
  const minDocumentsPanelWidth = 180;
  const maxDocumentsPanelWidth = 420;
  const minChatPanelWidth = 360;
  const abortControllerRef = useRef<AbortController | null>(null);
  const chatLayoutRef = useRef<HTMLDivElement | null>(null);
  const previousDefaultModelRef = useRef<string | null>(defaultModel);
  const wasActiveRef = useRef(isActive);
  const [selectedModel, setSelectedModel] = useState(() => loadChatSession()?.selectedModel ?? "");
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);

  const [selectedDocumentIds, setSelectedDocumentIds] = useState<number[]>(() => loadChatSession()?.selectedDocumentIds ?? []);
  const [conversationId, setConversationId] = useState<string | null>(() => loadChatSession()?.conversationId ?? null);
  const [messages, setMessages] = useState<ChatMessage[]>(() => loadChatSession()?.messages ?? []);
  const [input, setInput] = useState("");
  const [documentsPanelWidth, setDocumentsPanelWidth] = useState(() => loadDocumentsPanelWidth(minDocumentsPanelWidth, maxDocumentsPanelWidth));
  const [isResizingDocumentsPanel, setIsResizingDocumentsPanel] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isDocsLoading, setIsDocsLoading] = useState(true);
  const [documentsError, setDocumentsError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<string | null>(null);
  const [notices, setNotices] = useState<string[]>([]);
  const preview = useDocumentPreviewController();

  useEffect(() => {
    setInput(loadChatDraft());
  }, []);

  useEffect(() => {
    saveOrClearChatDraft(input);
  }, [input]);

  useEffect(() => {
    saveChatSession({ conversationId, messages, selectedDocumentIds, selectedModel: selectedModel || null });
  }, [conversationId, messages, selectedDocumentIds, selectedModel]);

  useEffect(() => {
    saveDocumentsPanelWidth(documentsPanelWidth, minDocumentsPanelWidth, maxDocumentsPanelWidth);
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
      const nextWidth = clampNumber(
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
    if (modelNames.length === 0) {
      return;
    }

    setSelectedModel((current) => {
      const previousDefaultModel = previousDefaultModelRef.current;
      previousDefaultModelRef.current = defaultModel;

      if (
        defaultModel
        && modelNames.includes(defaultModel)
        && defaultModel !== previousDefaultModel
      ) {
        return defaultModel;
      }

      if (current && modelNames.includes(current)) {
        return current;
      }

      return defaultModel && modelNames.includes(defaultModel) ? defaultModel : modelNames[0];
    });
  }, [defaultModel, models]);

  const refreshDocuments = useCallback(async (options: { isCancelled?: () => boolean } = {}) => {
    try {
      const docs = await apiRequest<ImportedDocument[]>("/api/documents");
      if (options.isCancelled?.()) {
        return;
      }

      setDocuments(docs);
      setSelectedDocumentIds((current) => {
        const available = new Set(docs.map((document) => document.id));
        return current.filter((id) => available.has(id));
      });
      setDocumentsError(null);
    } catch (error) {
      if (!options.isCancelled?.()) {
        setDocumentsError(error instanceof Error ? error.message : "Documenti non disponibili.");
      }
    } finally {
      if (!options.isCancelled?.()) {
        setIsDocsLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    let isCancelled = false;

    void refreshDocuments({ isCancelled: () => isCancelled });

    return () => {
      isCancelled = true;
    };
  }, [documentLibraryVersion, refreshDocuments]);

  useEffect(() => {
    if (isActive && !wasActiveRef.current) {
      void refreshDocuments();
    }
    wasActiveRef.current = isActive;
  }, [isActive, refreshDocuments]);

  useEffect(() => {
    setExitContributor("chat", {
      label: "Chat",
      hasPendingChanges: input.trim().length > 0,
      hasActiveWork: isGenerating,
      prepareForExit: async () => {
        abortControllerRef.current?.abort();
        saveOrClearChatDraft(input);
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
    const assistantMessageId = crypto.randomUUID();
    const assistantPlaceholder: ChatMessage = {
      id: assistantMessageId,
      role: "assistant",
      content: "",
      sources: []
    };

    setMessages((current) => [...current, userMessage, assistantPlaceholder]);
    setInput("");
    setFeedback(null);
    setNotices([]);
    setIsGenerating(true);

    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    try {
      await apiStreamRequest<ChatStreamChunkEvent>(
        "/api/chat/stream",
        {
          message,
          model: selectedModel,
          useDocuments: selectedDocumentIds.length > 0,
          selectedDocumentIds,
          conversationId
        },
        (evt) => {
          if (evt.eventType === "meta") {
            if (evt.conversationId) setConversationId(evt.conversationId);
            if (evt.sources) {
              setMessages((current) =>
                current.map((msg) =>
                  msg.id === assistantMessageId ? { ...msg, sources: evt.sources! } : msg
                )
              );
            }
            if (evt.notices) {
              setNotices(evt.notices.map((n) => n.message));
            }
          } else if (evt.eventType === "chunk" && evt.content) {
            setMessages((current) =>
              current.map((msg) =>
                msg.id === assistantMessageId
                  ? { ...msg, content: msg.content + evt.content }
                  : msg
              )
            );
          } else if (evt.eventType === "error" && evt.content) {
            setFeedback(evt.content);
          }
        },
        abortController.signal
      );
      clearChatDraft();
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
      return clampNumber(current + delta, minDocumentsPanelWidth, maxAllowedWidth);
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
    clearChatDraft();
  }

  async function handleOpenSource(source: ChatSource) {
    try {
      const document = documents.find((candidate) => candidate.id === source.documentId)
        ?? await apiRequest<ImportedDocument>(`/api/documents/${source.documentId}`);
      await preview.loadPreviewPage(document, source.pageStart ?? 1);
    } catch (error) {
      setFeedback(error instanceof Error ? error.message : "Fonte non apribile.");
    }
  }

  const canSend = Boolean(ollamaStatus?.isReachable && models.length > 0 && selectedModel && !isGenerating);

  return (
    <div
      className={isResizingDocumentsPanel ? "chat-layout chat-layout--resizing" : "chat-layout"}
      ref={chatLayoutRef}
      style={{ "--chat-documents-width": `${documentsPanelWidth}px` } as CSSProperties}
    >
      <ChatDocumentsPanel
        documentsError={documentsError}
        isDocsLoading={isDocsLoading}
        documents={documents}
        selectedDocumentIds={selectedDocumentIds}
        onToggleDocument={toggleDocument}
      />

      <ChatResizeHandle
        minDocumentsPanelWidth={minDocumentsPanelWidth}
        maxDocumentsPanelWidth={maxDocumentsPanelWidth}
        documentsPanelWidth={documentsPanelWidth}
        onPointerDown={() => setIsResizingDocumentsPanel(true)}
        onKeyDown={handleResizeHandleKeyDown}
      />

      <ChatMainPanel
        models={models}
        selectedModel={selectedModel}
        ollamaStatus={ollamaStatus}
        loadError={loadError}
        isGenerating={isGenerating}
        feedback={feedback}
        notices={notices}
        messages={messages}
        input={input}
        selectedDocumentIds={selectedDocumentIds}
        canSend={canSend}
        onSelectedModelChange={setSelectedModel}
        onNewChat={handleNewChat}
        onCancel={handleCancel}
        onInputChange={setInput}
        onInputKeyDown={handleInputKeyDown}
        onOpenSource={(source) => void handleOpenSource(source)}
        onSubmit={handleSubmit}
      />

      {preview.previewDocument && (
        <DocumentPreviewModal
          document={preview.previewDocument}
          preview={preview.previewData}
          isLoading={preview.isLoadingPreview}
          onClose={preview.handleClosePreview}
          onPageChange={(page) => void preview.loadPreviewPage(preview.previewDocument!, page)}
        />
      )}
    </div>
  );
}
