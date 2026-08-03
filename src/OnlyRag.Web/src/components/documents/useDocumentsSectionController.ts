import { useEffect, useRef, useState, type ChangeEvent, type DragEvent } from "react";
import {
  apiRequest,
  type DocumentEmbeddingStatus,
  type DocumentImportResponse,
  type ImportedDocument,
  type OcrPolicy,
  type OcrProcessingSettings
} from "../../api";
import { clearExitContributor, setExitContributor } from "../../appLifecycle";
import {
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  type RefreshStatus
} from "../../pollingStatus";
import {
  DEFAULT_OCR_LANGUAGE,
  anyOcrCandidate,
  fallbackOcrLanguages,
  hasBlockingDocumentJob,
  type FeedbackState,
  type PendingImport,
  type PendingOcrAction
} from "./DocumentsSection.controllerHelpers";
import { isOcrCandidate } from "./DocumentsSection.helpers";
import { useDocumentStatusPolling } from "./useDocumentStatusPolling";
import { useDocumentPreviewController } from "./useDocumentPreviewController";

import {
  useDocumentListQuery,
  useInvalidateDocuments,
  useOcrLanguagesQuery,
  useOcrSettingsQuery,
  useVectorHealthQuery
} from "../../hooks/useDocumentQueries";

type UseDocumentsSectionControllerOptions = {
  onLibraryChanged?: () => void;
};

export function useDocumentsSectionController({ onLibraryChanged }: UseDocumentsSectionControllerOptions = {}) {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const invalidateDocuments = useInvalidateDocuments();

  const { data: documentsData, isLoading, error: docsError } = useDocumentListQuery();
  const { data: vectorHealthData } = useVectorHealthQuery();
  const { data: ocrLanguagesData } = useOcrLanguagesQuery();
  const { data: ocrSettingsData } = useOcrSettingsQuery();

  const [overrideOcrLanguage, setOverrideOcrLanguage] = useState<string | null>(null);
  const [overrideOcrSettings, setOverrideOcrSettings] = useState<OcrProcessingSettings | null>(null);

  const documents = documentsData ?? [];
  const vectorHealth = vectorHealthData ?? null;
  const ocrLanguages = ocrLanguagesData ?? fallbackOcrLanguages;
  const ocrProcessingSettings = overrideOcrSettings || ocrSettingsData || null;
  const ocrDefaultLanguage = overrideOcrLanguage || ocrSettingsData?.language || DEFAULT_OCR_LANGUAGE;

  const [selectedDocument, setSelectedDocument] = useState<ImportedDocument | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [busyDocumentId, setBusyDocumentId] = useState<number | null>(null);
  const [isDragActive, setIsDragActive] = useState(false);
  const [feedback, setFeedback] = useState<FeedbackState>(null);
  const [pendingImport, setPendingImport] = useState<PendingImport | null>(null);
  const [pendingOcrAction, setPendingOcrAction] = useState<PendingOcrAction | null>(null);

  const [documentRefreshStatus, setDocumentRefreshStatus] = useState<RefreshStatus>(initialRefreshStatus);

  useEffect(() => {
    if (docsError) {
      setDocumentRefreshStatus((prev) => markRefreshFailure(prev, docsError.message));
    } else if (documentsData) {
      setDocumentRefreshStatus(markRefreshSuccess());
    }
  }, [documentsData, docsError]);

  const {
    detailRefreshStatus,
    embeddingStatus,
    ocrStatus,
    pipelineStatus,
    selectedJob,
    setDetailRefreshStatus
  } = useDocumentStatusPolling(selectedDocument);
  const preview = useDocumentPreviewController();

  useEffect(() => {
    if (documents.length > 0) {
      setSelectedDocument((current) =>
        current ? (documents.find((d) => d.id === current.id) ?? documents[0] ?? null) : (documents[0] ?? null)
      );
    }
  }, [documents]);

  useEffect(() => {
    setExitContributor("documents", {
      label: "Documenti",
      hasPendingChanges: false,
      hasActiveWork: isUploading || busyDocumentId !== null
    });
    return () => clearExitContributor("documents");
  }, [busyDocumentId, isUploading]);

  async function refreshDocuments(preferredId?: number | null) {
    invalidateDocuments();
    const docs = await apiRequest<ImportedDocument[]>("/api/documents");
    const nextId = preferredId ?? selectedDocument?.id ?? null;
    if (nextId === null) {
      setSelectedDocument(docs[0] ?? null);
      return;
    }
    const found = docs.find((d) => d.id === nextId);
    if (!found) {
      setSelectedDocument(docs[0] ?? null);
      return;
    }
    const detail = await apiRequest<ImportedDocument>(`/api/documents/${found.id}`);
    setSelectedDocument(detail);
    setDetailRefreshStatus(markRefreshSuccess());
  }

  async function importFiles(files: FileList | File[], policy: OcrPolicy, ocrLanguage: string = ocrDefaultLanguage) {
    if (files.length === 0) return;
    setIsUploading(true);
    setFeedback(null);
    try {
      const formData = new FormData();
      for (const file of Array.from(files)) formData.append("files", file, file.name);
      formData.append("ocrPolicy", policy);
      formData.append("ocrLanguage", ocrLanguage);
      const response = await apiRequest<DocumentImportResponse>("/api/documents/import", { method: "POST", body: formData });
      const imported = response.documents;
      const lastId = imported[imported.length - 1]?.document.id ?? null;
      if (lastId !== null) {
        await refreshDocuments(lastId);
      } else {
        await refreshDocuments(selectedDocument?.id ?? null);
      }
      onLibraryChanged?.();
      const dedupCount = imported.filter((d) => d.deduplicated).length;
      const importCount = imported.length - dedupCount;
      const failed = response.results.filter((result) => !result.succeeded);
      const failedSummary = failed.length > 0
        ? ` ${formatFileCount(failed.length, "non importato", "non importati")}: ${failed.map((result) => result.fileName).join(", ")}.`
        : "";
      const successSummary = dedupCount > 0
        ? `${formatFileCount(importCount, "importato", "importati")}, ${formatFileCount(dedupCount, "duplicato riutilizzato", "duplicati riutilizzati")}.`
        : `${formatFileCount(importCount, "importato", "importati")}. Analisi e indicizzazione in corso.`;
      setFeedback({
        tone: imported.length > 0 ? "info" : "error",
        message: imported.length > 0
          ? `${successSummary}${failedSummary}`
          : failedSummary.trim() || "Nessun file importato."
      });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Import documento non riuscito." });
    } finally {
      setIsUploading(false);
      if (fileInputRef.current) fileInputRef.current.value = "";
    }
  }

  function handleFilesSelected(files: FileList | File[]) {
    if (anyOcrCandidate(files)) {
      setPendingImport({ files });
    } else {
      void importFiles(files, "Auto", ocrDefaultLanguage);
    }
  }

  function handleOcrChoice(policy: OcrPolicy | "cancel", ocrLanguage: string = ocrDefaultLanguage) {
    if (policy === "cancel" || !pendingImport) {
      setPendingImport(null);
      return;
    }
    const { files } = pendingImport;
    setPendingImport(null);
    void rememberDocumentOcrLanguage(ocrLanguage);
    void importFiles(files, policy, ocrLanguage);
  }

  async function handleSelectDocument(id: number) {
    setBusyDocumentId(id);
    setFeedback(null);
    try {
      const detail = await apiRequest<ImportedDocument>(`/api/documents/${id}`);
      setSelectedDocument(detail);
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Dettagli documento non disponibili." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  function handleReindex(document: ImportedDocument) {
    if (isOcrCandidate(document)) {
      setPendingOcrAction({ kind: "reindex", document });
      return;
    }

    void executeReindex(document, ocrDefaultLanguage);
  }

  async function executeReindex(document: ImportedDocument, ocrLanguage: string) {
    if (busyDocumentId === document.id) return;
    if (await hasBlockingDocumentJob(document)) {
      setFeedback({ tone: "info", message: `Un job è già in corso per ${document.originalFileName}. Attendi il completamento.` });
      return;
    }
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      await apiRequest<ImportedDocument>(
        `/api/documents/${document.id}/reindex?ocrLanguage=${encodeURIComponent(ocrLanguage)}`,
        { method: "POST" }
      );
      await refreshDocuments(document.id);
      onLibraryChanged?.();
      setFeedback({ tone: "info", message: `Reindicizzazione avviata per ${document.originalFileName}.` });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Reindicizzazione non riuscita." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  async function handleEmbed(document: ImportedDocument) {
    if (busyDocumentId === document.id) return;
    if (document.chunkCount === 0) {
      setFeedback({ tone: "error", message: `${document.originalFileName} non ha chunk: esegui prima "Ricostruisci indice".` });
      return;
    }
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      await apiRequest<DocumentEmbeddingStatus>(`/api/documents/${document.id}/embed`, { method: "POST" });
      await refreshDocuments(document.id);
      onLibraryChanged?.();
      setFeedback({ tone: "info", message: `Rigenera ricerca semantica avviata per ${document.originalFileName}.` });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Embedding non avviati." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  function handleRunOcr(document: ImportedDocument, force: boolean = false) {
    setPendingOcrAction({ kind: "ocr", document, force });
  }

  async function executeRunOcr(document: ImportedDocument, force: boolean, ocrLanguage: string) {
    if (busyDocumentId === document.id) return;
    if (await hasBlockingDocumentJob(document)) {
      setFeedback({ tone: "info", message: `Un job è già in corso per ${document.originalFileName}.` });
      return;
    }
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      const query = new URLSearchParams({
        force: String(force),
        ocrLanguage
      });
      await apiRequest<ImportedDocument>(`/api/documents/${document.id}/ocr?${query.toString()}`, { method: "POST" });
      await refreshDocuments(document.id);
      onLibraryChanged?.();
      setFeedback({
        tone: "info",
        message: force
          ? `Rilettura completa con OCR avviata per ${document.originalFileName}. Testo, indice e ricerca semantica saranno rigenerati.`
          : `Riesecuzione OCR avviata per ${document.originalFileName}.`
      });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "OCR non avviato." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  function handleOcrActionLanguage(language: string | "cancel") {
    if (language === "cancel" || !pendingOcrAction) {
      setPendingOcrAction(null);
      return;
    }

    const action = pendingOcrAction;
    setPendingOcrAction(null);
    void rememberDocumentOcrLanguage(language);
    if (action.kind === "reindex") {
      void executeReindex(action.document, language);
      return;
    }

    void executeRunOcr(action.document, action.force, language);
  }

  async function rememberDocumentOcrLanguage(language: string) {
    const normalizedLanguage = language.trim();
    if (normalizedLanguage.length === 0) {
      return;
    }

    setOverrideOcrLanguage(normalizedLanguage);

    const currentSettings = ocrProcessingSettings ?? {
      language: DEFAULT_OCR_LANGUAGE,
      maxRetries: 2,
      pageTimeoutSeconds: 180,
      lowConfidenceThreshold: 0.55
    };
    const nextSettings = { ...currentSettings, language: normalizedLanguage };
    setOverrideOcrSettings(nextSettings);

    try {
      await apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing", {
        method: "PUT",
        body: JSON.stringify(nextSettings)
      });
    } catch {
      // The selected language is still used for the current operation; persistence can retry later.
    }
  }

  async function handleDelete(document: ImportedDocument) {
    if (!window.confirm(`Eliminare definitivamente "${document.originalFileName}"?`)) return;
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      await apiRequest<ImportedDocument>(`/api/documents/${document.id}`, { method: "DELETE" });
      await refreshDocuments(selectedDocument?.id === document.id ? null : selectedDocument?.id ?? null);
      onLibraryChanged?.();
      setFeedback({ tone: "info", message: `${document.originalFileName} eliminato.` });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Eliminazione non riuscita." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  function handleBrowseClick() {
    fileInputRef.current?.click();
  }

  function handleInputChange(e: ChangeEvent<HTMLInputElement>) {
    if (e.target.files) handleFilesSelected(e.target.files);
  }

  function handleDragOver(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragActive(true);
  }

  function handleDragLeave(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    if (e.currentTarget.contains(e.relatedTarget as Node | null)) return;
    setIsDragActive(false);
  }

  function handleDrop(e: DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setIsDragActive(false);
    if (e.dataTransfer.files.length > 0) handleFilesSelected(e.dataTransfer.files);
  }

  const canPreview = selectedDocument?.status === "Indexed" && (selectedDocument?.pageCount ?? 0) > 0;

  return {
    busyDocumentId,
    canPreview,
    detailRefreshStatus,
    documentRefreshStatus,
    documents,
    embeddingStatus,
    feedback,
    fileInputRef,
    handleBrowseClick,
    handleClosePreview: preview.handleClosePreview,
    handleDelete,
    handleDragLeave,
    handleDragOver,
    handleDrop,
    handleEmbed,
    handleInputChange,
    handleOcrActionLanguage,
    handleOcrChoice,
    handleOpenPreview: preview.handleOpenPreview,
    handleReindex,
    handleRunOcr,
    handleSelectDocument,
    isDragActive,
    isLoading,
    isLoadingPreview: preview.isLoadingPreview,
    isUploading,
    ocrDefaultLanguage,
    ocrLanguages,
    ocrStatus,
    pendingImport,
    pendingOcrAction,
    pipelineStatus,
    previewData: preview.previewData,
    previewDocument: preview.previewDocument,
    selectedDocument,
    selectedJob,
    vectorHealth,
    loadPreviewPage: preview.loadPreviewPage
  };
}

function formatFileCount(count: number, singular: string, plural: string): string {
  return `${count} file ${count === 1 ? singular : plural}`;
}
