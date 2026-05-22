import { useEffect, useRef, useState, type ChangeEvent, type DragEvent } from "react";
import {
  apiRequest,
  type DocumentEmbeddingStatus,
  type DocumentImportResponse,
  type DocumentOcrStatus,
  type DocumentPipelineStatus,
  type DocumentPreviewResponse,
  type ImportedDocument,
  type LocalJob,
  type OcrLanguage,
  type OcrPolicy,
  type VectorBackendHealth
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import {
  formatLastRefresh,
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  shouldSurfaceRefreshFailure
} from "../pollingStatus";
import { DocumentPreviewModal } from "./DocumentPreviewModal";
import { ProgressBar } from "./ProgressBar";
import {
  DocumentDetailCard,
  DocumentListRow,
  OcrChoiceDialog,
  OcrLanguageDialog,
  isOcrCandidate
} from "./DocumentsSection.helpers";
// ---------------------------------------------------------------------------
// OCR candidate detection (client-side, by extension)
// ---------------------------------------------------------------------------
const OCR_EXTENSIONS = new Set([".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"]);

function anyOcrCandidate(files: FileList | File[]): boolean {
  for (const file of Array.from(files)) {
    const ext = ("." + file.name.split(".").pop()!).toLowerCase();
    if (OCR_EXTENSIONS.has(ext)) return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// Feedback
// ---------------------------------------------------------------------------
type FeedbackTone = "info" | "error";
type FeedbackState = { tone: FeedbackTone; message: string } | null;
const DEFAULT_OCR_LANGUAGE = "it";
const fallbackOcrLanguages: OcrLanguage[] = [
  { code: "it", label: "Italiano", scriptGroup: "Principali", isDefault: true }
];

// ---------------------------------------------------------------------------
// OCR dialog state
// ---------------------------------------------------------------------------
type PendingImport = { files: FileList | File[] };
type PendingOcrAction =
  | { kind: "reindex"; document: ImportedDocument }
  | { kind: "ocr"; document: ImportedDocument; force: boolean };

// ---------------------------------------------------------------------------
// Main component
// ---------------------------------------------------------------------------
export function DocumentsSection() {
  const fileInputRef = useRef<HTMLInputElement | null>(null);
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);
  const [selectedDocument, setSelectedDocument] = useState<ImportedDocument | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isUploading, setIsUploading] = useState(false);
  const [busyDocumentId, setBusyDocumentId] = useState<number | null>(null);
  const [selectedJob, setSelectedJob] = useState<LocalJob | null>(null);
  const [embeddingStatus, setEmbeddingStatus] = useState<DocumentEmbeddingStatus | null>(null);
  const [ocrStatus, setOcrStatus] = useState<DocumentOcrStatus | null>(null);
  const [pipelineStatus, setPipelineStatus] = useState<DocumentPipelineStatus | null>(null);
  const [isDragActive, setIsDragActive] = useState(false);
  const [feedback, setFeedback] = useState<FeedbackState>(null);
  const [vectorHealth, setVectorHealth] = useState<VectorBackendHealth | null>(null);
  const [pendingImport, setPendingImport] = useState<PendingImport | null>(null);
  const [pendingOcrAction, setPendingOcrAction] = useState<PendingOcrAction | null>(null);
  const [ocrLanguages, setOcrLanguages] = useState<OcrLanguage[]>(fallbackOcrLanguages);
  const [previewDocument, setPreviewDocument] = useState<ImportedDocument | null>(null);
  const [previewData, setPreviewData] = useState<DocumentPreviewResponse | null>(null);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);
  const [documentRefreshStatus, setDocumentRefreshStatus] = useState(initialRefreshStatus);
  const [detailRefreshStatus, setDetailRefreshStatus] = useState(initialRefreshStatus);

  useEffect(() => {
    setExitContributor("documents", {
      label: "Documenti",
      hasPendingChanges: false,
      hasActiveWork: isUploading || busyDocumentId !== null
    });
    return () => clearExitContributor("documents");
  }, [busyDocumentId, isUploading]);

  useEffect(() => {
    apiRequest<VectorBackendHealth>("/api/diagnostics/vector-health")
      .then((health) => setVectorHealth(health))
      .catch(() => {});
    apiRequest<OcrLanguage[]>("/api/ocr/languages")
      .then((languages) => setOcrLanguages(languages.length > 0 ? languages : fallbackOcrLanguages))
      .catch(() => setOcrLanguages(fallbackOcrLanguages));
  }, []);

  // Initial load
  useEffect(() => {
    let cancelled = false;
    apiRequest<ImportedDocument[]>("/api/documents")
      .then((docs) => {
        if (cancelled) return;
        setDocuments(docs);
        setSelectedDocument(docs[0] ?? null);
        setDocumentRefreshStatus(markRefreshSuccess());
      })
      .catch((err) => {
        if (!cancelled) {
          const message = err instanceof Error ? err.message : "Impossibile leggere i documenti.";
          setFeedback({ tone: "error", message });
          setDocumentRefreshStatus((current) => markRefreshFailure(current, message));
        }
      })
      .finally(() => { if (!cancelled) setIsLoading(false); });
    return () => { cancelled = true; };
  }, []);

  // Polling document list
  useEffect(() => {
    let cancelled = false;
    const interval = window.setInterval(async () => {
      try {
        const docs = await apiRequest<ImportedDocument[]>("/api/documents");
        if (cancelled) return;
        setDocuments(docs);
        setSelectedDocument((current) =>
          current ? (docs.find((d) => d.id === current.id) ?? current) : (docs[0] ?? null)
        );
        setDocumentRefreshStatus(markRefreshSuccess());
      } catch (error) {
        if (cancelled) return;
        const message = error instanceof Error ? error.message : "Impossibile aggiornare i documenti.";
        setDocumentRefreshStatus((current) => markRefreshFailure(current, message));
      }
    }, 5000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, []);

  // Poll job for selected document
  useEffect(() => {
    let cancelled = false;
    const jobId = selectedDocument?.currentJobId;
    if (!jobId) { setSelectedJob(null); setDetailRefreshStatus(initialRefreshStatus); return; }
    const fetch = () => apiRequest<LocalJob>(`/api/jobs/${jobId}`)
      .then((job) => {
        if (!cancelled) {
          setSelectedJob(job);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : "Impossibile aggiornare il job selezionato.";
          setDetailRefreshStatus((current) => markRefreshFailure(current, message));
        }
      });
    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [selectedDocument?.currentJobId]);

  // Poll embedding status for selected document
  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) { setEmbeddingStatus(null); return; }
    const fetch = () => apiRequest<DocumentEmbeddingStatus>(`/api/documents/${id}/embedding-status`)
      .then((s) => {
        if (!cancelled) {
          setEmbeddingStatus(s);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : "Impossibile aggiornare lo stato embedding.";
          setDetailRefreshStatus((current) => markRefreshFailure(current, message));
        }
      });
    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [selectedDocument?.id]);

  // Poll OCR status for selected document
  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) { setOcrStatus(null); return; }
    const fetch = () => apiRequest<DocumentOcrStatus>(`/api/documents/${id}/ocr-status`)
      .then((s) => {
        if (!cancelled) {
          setOcrStatus(s);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : "Impossibile aggiornare lo stato OCR.";
          setDetailRefreshStatus((current) => markRefreshFailure(current, message));
        }
      });
    void fetch();
    const interval = window.setInterval(fetch, 3000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [selectedDocument?.id]);

  // Poll pipeline status for selected document
  useEffect(() => {
    let cancelled = false;
    const id = selectedDocument?.id;
    if (!id) { setPipelineStatus(null); return; }
    const fetch = () => apiRequest<DocumentPipelineStatus>(`/api/documents/${id}/pipeline-status`)
      .then((s) => {
        if (!cancelled) {
          setPipelineStatus(s);
          setDetailRefreshStatus(markRefreshSuccess());
        }
      })
      .catch((error) => {
        if (!cancelled) {
          const message = error instanceof Error ? error.message : "Impossibile aggiornare lo stato pipeline.";
          setDetailRefreshStatus((current) => markRefreshFailure(current, message));
        }
      });
    void fetch();
    const interval = window.setInterval(fetch, 4000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [selectedDocument?.id]);

  async function refreshDocuments(preferredId?: number | null) {
    const docs = await apiRequest<ImportedDocument[]>("/api/documents");
    setDocuments(docs);
    setDocumentRefreshStatus(markRefreshSuccess());
    const nextId = preferredId ?? selectedDocument?.id ?? null;
    if (nextId === null) { setSelectedDocument(docs[0] ?? null); return; }
    const found = docs.find((d) => d.id === nextId);
    if (!found) { setSelectedDocument(docs[0] ?? null); return; }
    const detail = await apiRequest<ImportedDocument>(`/api/documents/${found.id}`);
    setSelectedDocument(detail);
    setDetailRefreshStatus(markRefreshSuccess());
  }

  async function importFiles(files: FileList | File[], policy: OcrPolicy, ocrLanguage: string = DEFAULT_OCR_LANGUAGE) {
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
      await refreshDocuments(lastId);
      const dedupCount = imported.filter((d) => d.deduplicated).length;
      const importCount = imported.length - dedupCount;
      setFeedback({
        tone: "info",
        message: dedupCount > 0
          ? `${importCount} file importati, ${dedupCount} duplicati riutilizzati.`
          : `${importCount} file importati. Analisi e indicizzazione in corso.`
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
      void importFiles(files, "Auto", DEFAULT_OCR_LANGUAGE);
    }
  }

  function handleOcrChoice(policy: OcrPolicy | "cancel", ocrLanguage: string = DEFAULT_OCR_LANGUAGE) {
    if (policy === "cancel" || !pendingImport) { setPendingImport(null); return; }
    const { files } = pendingImport;
    setPendingImport(null);
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

    void executeReindex(document, DEFAULT_OCR_LANGUAGE);
  }

  async function executeReindex(document: ImportedDocument, ocrLanguage: string) {
    if (busyDocumentId === document.id) return;
    if (document.currentJobId) {
      const job = await apiRequest<LocalJob>(`/api/jobs/${document.currentJobId}`).catch(() => null);
      if (job?.status === "Running" || job?.status === "Pausing" || job?.status === "Pending") {
        setFeedback({ tone: "info", message: `Un job è già in corso per ${document.originalFileName}. Attendi il completamento.` });
        return;
      }
    }
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      await apiRequest<ImportedDocument>(
        `/api/documents/${document.id}/reindex?ocrLanguage=${encodeURIComponent(ocrLanguage)}`,
        { method: "POST" }
      );
      await refreshDocuments(document.id);
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
    if (document.currentJobId) {
      const job = await apiRequest<LocalJob>(`/api/jobs/${document.currentJobId}`).catch(() => null);
      if (job?.status === "Running" || job?.status === "Pausing" || job?.status === "Pending") {
        setFeedback({ tone: "info", message: `Un job è già in corso per ${document.originalFileName}.` });
        return;
      }
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
    if (action.kind === "reindex") {
      void executeReindex(action.document, language);
      return;
    }

    void executeRunOcr(action.document, action.force, language);
  }

  async function handleDelete(document: ImportedDocument) {
    if (!window.confirm(`Eliminare definitivamente "${document.originalFileName}"?`)) return;
    setBusyDocumentId(document.id);
    setFeedback(null);
    try {
      await apiRequest<ImportedDocument>(`/api/documents/${document.id}`, { method: "DELETE" });
      await refreshDocuments(selectedDocument?.id === document.id ? null : selectedDocument?.id ?? null);
      setFeedback({ tone: "info", message: `${document.originalFileName} eliminato.` });
    } catch (err) {
      setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Eliminazione non riuscita." });
    } finally {
      setBusyDocumentId(null);
    }
  }

  async function loadPreviewPage(document: ImportedDocument, page: number) {
    setIsLoadingPreview(true);
    setPreviewDocument(document);
    try {
      const pageStart = Math.max(1, Math.min(page, Math.max(1, document.pageCount)));
      const data = await apiRequest<DocumentPreviewResponse>(
        `/api/documents/${document.id}/preview?page=${pageStart}&pageSize=1`
      );
      setPreviewData(data);
    } catch {
      setPreviewData(null);
    } finally {
      setIsLoadingPreview(false);
    }
  }

  async function handleOpenPreview(document: ImportedDocument) {
    await loadPreviewPage(document, 1);
  }

  function handleClosePreview() {
    setPreviewDocument(null);
    setPreviewData(null);
  }

  function handleBrowseClick() { fileInputRef.current?.click(); }

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

  return (
    <div className="documents-panel">
      {/* OCR policy dialog */}
      {pendingImport && (
        <OcrChoiceDialog
          fileCount={Array.from(pendingImport.files).length}
          languages={ocrLanguages}
          onChoice={handleOcrChoice}
        />
      )}

      {pendingOcrAction && (
        <OcrLanguageDialog
          documentName={pendingOcrAction.document.originalFileName}
          actionLabel={pendingOcrAction.kind === "reindex"
            ? "Ricostruisci indice"
            : pendingOcrAction.force
              ? "Rileggi tutto con OCR"
              : "Riesegui lettura testo"}
          languages={ocrLanguages}
          onChoice={handleOcrActionLanguage}
        />
      )}

      {/* Preview modal */}
      {previewDocument && (
        <DocumentPreviewModal
          document={previewDocument}
          preview={previewData}
          isLoading={isLoadingPreview}
          onClose={handleClosePreview}
          onPageChange={(page) => void loadPreviewPage(previewDocument, page)}
        />
      )}

      {/* Drop zone */}
      <div
        className={isDragActive ? "document-dropzone document-dropzone--active" : "document-dropzone"}
        onDragLeave={handleDragLeave}
        onDragOver={handleDragOver}
        onDrop={handleDrop}
      >
        <strong>Trascina qui i file da importare</strong>
        <div className="settings-actions">
          <button disabled={isUploading} type="button" onClick={handleBrowseClick}>
            Sfoglia file
          </button>
        </div>
        {isUploading && (
          <div className="document-dropzone__progress">
            <ProgressBar label="Importazione in corso..." value={0} indeterminate />
          </div>
        )}
        <input
          ref={fileInputRef}
          hidden
          multiple
          type="file"
          accept=".txt,.md,.markdown,.csv,.pdf,.png,.jpg,.jpeg,.tif,.tiff,.bmp,.gif,.webp,.docx,.xlsx,.pptx,.doc,.xls,.ppt"
          onChange={handleInputChange}
        />
      </div>

      {vectorHealth?.nearLimit && (
        <div className="panel-note panel-note--warning" role="alert">
          <p>{vectorHealth.warning ?? `Backend vettoriale: ${vectorHealth.totalVectors}/${vectorHealth.vectorLimit} vettori usati.`}</p>
        </div>
      )}

      {feedback && (
        <div
          className={`feedback-banner feedback-banner--${feedback.tone}`}
          role={feedback.tone === "error" ? "alert" : "status"}
        >
          {feedback.message}
        </div>
      )}

      <div className="documents-main-layout">
        {/* Document list */}
        <div className="documents-list-card">
          <div className="documents-toolbar">
            <strong>Documenti importati</strong>
            <span>{documents.length}</span>
            <small>Ultimo aggiornamento: {formatLastRefresh(documentRefreshStatus.lastSuccessfulRefreshAt)}</small>
          </div>
          {shouldSurfaceRefreshFailure(documentRefreshStatus) && (
            <div className="feedback-banner feedback-banner--error" role="alert">
              {documentRefreshStatus.lastErrorMessage} Stato non aggiornato da {formatLastRefresh(documentRefreshStatus.lastSuccessfulRefreshAt)}.
            </div>
          )}
          {isLoading ? (
            <div className="empty-state"><p>Caricamento documenti...</p></div>
          ) : documents.length === 0 ? (
            <div className="empty-state"><p>Nessun documento presente. Importa un file per iniziare.</p></div>
          ) : (
            <div className="documents-list" aria-label="Documenti importati">
              {documents.map((doc) => (
                <DocumentListRow
                  key={doc.id}
                  document={doc}
                  isSelected={selectedDocument?.id === doc.id}
                  isBusy={busyDocumentId === doc.id}
                  onSelect={handleSelectDocument}
                />
              ))}
            </div>
          )}
        </div>

        {/* Document detail panel */}
        {selectedDocument && (
          <div className="document-detail-stack">
            {shouldSurfaceRefreshFailure(detailRefreshStatus) && (
              <div className="feedback-banner feedback-banner--error" role="alert">
                {detailRefreshStatus.lastErrorMessage} Dettaglio non aggiornato da {formatLastRefresh(detailRefreshStatus.lastSuccessfulRefreshAt)}.
              </div>
            )}
            <DocumentDetailCard
              document={selectedDocument}
              pipelineStatus={pipelineStatus}
              embeddingStatus={embeddingStatus}
              ocrStatus={ocrStatus}
              activeJob={selectedJob}
              isBusy={busyDocumentId === selectedDocument.id}
              canPreview={canPreview}
              onReindex={handleReindex}
              onEmbed={handleEmbed}
              onOcr={handleRunOcr}
              onDelete={handleDelete}
              onPreview={handleOpenPreview}
            />
          </div>
        )}
      </div>
    </div>
  );
}




