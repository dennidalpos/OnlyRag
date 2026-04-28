import { useEffect, useRef, useState, type ChangeEvent, type DragEvent, type KeyboardEvent } from "react";
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
  type PhaseState,
  type VectorBackendHealth
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import { ProgressBar } from "./ProgressBar";
import { DocumentPreviewModal } from "./DocumentPreviewModal";
import { useModalFocusTrap } from "./useModalFocusTrap";

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
// Labels
// ---------------------------------------------------------------------------
const phaseLabels: Record<string, string> = {
  import: "File importato",
  analysis: "Analisi",
  ocr: "OCR / lettura visiva",
  textExtraction: "Estrazione testo",
  chunking: "Indicizzazione",
  embedding: "Ricerca semantica"
};

const phaseStateIcon: Record<PhaseState, string> = {
  Completed: "✓",
  InProgress: "⟳",
  Failed: "✕",
  Skipped: "–",
  Todo: "○",
  Obsolete: "⚠"
};

const phaseStateClass: Record<PhaseState, string> = {
  Completed: "phase-completed",
  InProgress: "phase-in-progress",
  Failed: "phase-failed",
  Skipped: "phase-skipped",
  Todo: "phase-todo",
  Obsolete: "phase-obsolete"
};

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

  useEffect(() => {
    setExitContributor("documents", {
      label: "Documenti",
      hasPendingChanges: false,
      hasActiveWork: isUploading || busyDocumentId !== null
    });
    return () => clearExitContributor("documents");
  }, [busyDocumentId, isUploading]);

  useEffect(() => {
    apiRequest<VectorBackendHealth>("/api/health")
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
      })
      .catch((err) => {
        if (!cancelled) setFeedback({ tone: "error", message: err instanceof Error ? err.message : "Impossibile leggere i documenti." });
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
      } catch {}
    }, 5000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, []);

  // Poll job for selected document
  useEffect(() => {
    let cancelled = false;
    const jobId = selectedDocument?.currentJobId;
    if (!jobId) { setSelectedJob(null); return; }
    const fetch = () => apiRequest<LocalJob>(`/api/jobs/${jobId}`)
      .then((job) => { if (!cancelled) setSelectedJob(job); })
      .catch(() => { if (!cancelled) setSelectedJob(null); });
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
      .then((s) => { if (!cancelled) setEmbeddingStatus(s); })
      .catch(() => { if (!cancelled) setEmbeddingStatus(null); });
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
      .then((s) => { if (!cancelled) setOcrStatus(s); })
      .catch(() => { if (!cancelled) setOcrStatus(null); });
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
      .then((s) => { if (!cancelled) setPipelineStatus(s); })
      .catch(() => { if (!cancelled) setPipelineStatus(null); });
    void fetch();
    const interval = window.setInterval(fetch, 4000);
    return () => { cancelled = true; window.clearInterval(interval); };
  }, [selectedDocument?.id]);

  async function refreshDocuments(preferredId?: number | null) {
    const docs = await apiRequest<ImportedDocument[]>("/api/documents");
    setDocuments(docs);
    const nextId = preferredId ?? selectedDocument?.id ?? null;
    if (nextId === null) { setSelectedDocument(docs[0] ?? null); return; }
    const found = docs.find((d) => d.id === nextId);
    if (!found) { setSelectedDocument(docs[0] ?? null); return; }
    const detail = await apiRequest<ImportedDocument>(`/api/documents/${found.id}`);
    setSelectedDocument(detail);
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
      if (job?.status === "Running" || job?.status === "Pending") {
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
      if (job?.status === "Running" || job?.status === "Pending") {
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

  async function handleOpenPreview(document: ImportedDocument) {
    setIsLoadingPreview(true);
    setPreviewDocument(document);
    try {
      const data = await apiRequest<DocumentPreviewResponse>(`/api/documents/${document.id}/preview`);
      setPreviewData(data);
    } catch {
      setPreviewData(null);
    } finally {
      setIsLoadingPreview(false);
    }
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
          </div>
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
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// OCR choice dialog
// ---------------------------------------------------------------------------
function OcrChoiceDialog({
  fileCount,
  languages,
  onChoice
}: {
  fileCount: number;
  languages: OcrLanguage[];
  onChoice: (policy: OcrPolicy | "cancel", ocrLanguage?: string) => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getDefaultOcrLanguage(languages));
  useModalFocusTrap(dialogRef, true, { onEscape: () => onChoice("cancel") });

  return (
    <div className="modal-backdrop">
      <div
        className="ocr-choice-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Scegli modalità OCR"
        ref={dialogRef}
        tabIndex={-1}
      >
        <h3>Modalità di lettura testo</h3>
        <p>
          {fileCount === 1
            ? "Il file selezionato potrebbe essere una scansione o un PDF con immagini."
            : `${fileCount} file selezionati contengono PDF o immagini.`}
          {" "}Come vuoi estrarre il testo?
        </p>

        <OcrLanguageSelect
          languages={languages}
          value={selectedLanguage}
          onChange={setSelectedLanguage}
        />

        <div className="ocr-choice-options">
          <button
            className="ocr-choice-option"
            type="button"
            onClick={() => onChoice("Auto", selectedLanguage)}
          >
            <strong>Usa testo esistente</strong>
            <span>Legge il testo già incorporato nel file; usa OCR solo sulle pagine che lo richiedono.</span>
            <em>Consigliato per documenti con testo digitale.</em>
          </button>

          <button
            className="ocr-choice-option"
            type="button"
            onClick={() => onChoice("ForceAll", selectedLanguage)}
          >
            <strong>Rileggi tutto con OCR</strong>
            <span>Tratta ogni pagina come immagine e applica OCR completo, anche se contiene già testo.</span>
            <em>Consigliato per scansioni, documenti stampati o PDF protetti.</em>
          </button>
        </div>

        <div className="settings-actions" style={{ justifyContent: "flex-end" }}>
          <button className="button-secondary" type="button" onClick={() => onChoice("cancel")}>
            Annulla importazione
          </button>
        </div>
      </div>
    </div>
  );
}

function OcrLanguageDialog({
  documentName,
  actionLabel,
  languages,
  onChoice
}: {
  documentName: string;
  actionLabel: string;
  languages: OcrLanguage[];
  onChoice: (language: string | "cancel") => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getDefaultOcrLanguage(languages));
  useModalFocusTrap(dialogRef, true, { onEscape: () => onChoice("cancel") });

  return (
    <div className="modal-backdrop">
      <div
        className="ocr-choice-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Scegli lingua OCR"
        ref={dialogRef}
        tabIndex={-1}
      >
        <h3>Lingua documento</h3>
        <p>{actionLabel}: {documentName}</p>

        <OcrLanguageSelect
          languages={languages}
          value={selectedLanguage}
          onChange={setSelectedLanguage}
        />

        <div className="settings-actions" style={{ justifyContent: "flex-end" }}>
          <button className="button-secondary" type="button" onClick={() => onChoice("cancel")}>
            Annulla
          </button>
          <button type="button" onClick={() => onChoice(selectedLanguage)}>
            Avvia
          </button>
        </div>
      </div>
    </div>
  );
}

function OcrLanguageSelect({
  languages,
  value,
  onChange
}: {
  languages: OcrLanguage[];
  value: string;
  onChange: (language: string) => void;
}) {
  const grouped = languages.reduce<Record<string, OcrLanguage[]>>((groups, language) => {
    const group = language.scriptGroup || "Avanzate";
    groups[group] = [...(groups[group] ?? []), language];
    return groups;
  }, {});

  const groupNames = Object.keys(grouped).sort((left, right) => {
    if (left === "Principali") return -1;
    if (right === "Principali") return 1;
    return left.localeCompare(right);
  });

  return (
    <label className="field-group" htmlFor="ocr-language">
      <span>Lingua documento</span>
      <select
        id="ocr-language"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {groupNames.map((groupName) => (
          <optgroup key={groupName} label={groupName}>
            {grouped[groupName]
              .slice()
              .sort((left, right) => left.label.localeCompare(right.label))
              .map((language) => (
                <option key={language.code} value={language.code}>
                  {language.code} - {language.label}
                </option>
              ))}
          </optgroup>
        ))}
      </select>
    </label>
  );
}

// ---------------------------------------------------------------------------
// Document list row
// ---------------------------------------------------------------------------
function DocumentListRow({
  document,
  isSelected,
  isBusy,
  onSelect
}: {
  document: ImportedDocument;
  isSelected: boolean;
  isBusy: boolean;
  onSelect: (id: number) => void;
}) {
  function handleKeyDown(event: KeyboardEvent<HTMLElement>) {
    if (isBusy || (event.key !== "Enter" && event.key !== " ")) {
      return;
    }

    event.preventDefault();
    onSelect(document.id);
  }

  return (
    <article
      className={isSelected ? "document-row document-row--selected" : "document-row"}
      role="button"
      tabIndex={isBusy ? -1 : 0}
      aria-pressed={isSelected}
      aria-disabled={isBusy}
      onClick={() => !isBusy && onSelect(document.id)}
      onKeyDown={handleKeyDown}
    >
      <div className="document-row__header">
        <div>
          <strong>{document.originalFileName}</strong>
          <span>{formatDocumentType(document)}</span>
        </div>
        <span className={getDocumentStatusClassName(document.status)}>
          {statusLabels[document.status]}
        </span>
      </div>
      <div className="document-row__meta">
        <span>{formatFileSize(document.fileSizeBytes)}</span>
        <span>{new Date(document.createdAtUtc).toLocaleString()}</span>
      </div>
      {(document.chunkCount > 0 || document.pageCount > 0) && (
        <div className="document-row__meta">
          {document.pageCount > 0 && <span>{formatIndexedUnitLabel(document)}: {document.pageCount}</span>}
          {document.chunkCount > 0 && <span>Chunk: {document.chunkCount}</span>}
        </div>
      )}
    </article>
  );
}

// ---------------------------------------------------------------------------
// Document detail card
// ---------------------------------------------------------------------------
function DocumentDetailCard({
  document,
  pipelineStatus,
  embeddingStatus,
  ocrStatus,
  activeJob,
  isBusy,
  canPreview,
  onReindex,
  onEmbed,
  onOcr,
  onDelete,
  onPreview
}: {
  document: ImportedDocument;
  pipelineStatus: DocumentPipelineStatus | null;
  embeddingStatus: DocumentEmbeddingStatus | null;
  ocrStatus: DocumentOcrStatus | null;
  activeJob: LocalJob | null;
  isBusy: boolean;
  canPreview: boolean;
  onReindex: (doc: ImportedDocument) => void;
  onEmbed: (doc: ImportedDocument) => void;
  onOcr: (doc: ImportedDocument, force?: boolean) => void;
  onDelete: (doc: ImportedDocument) => void;
  onPreview: (doc: ImportedDocument) => void;
}) {
  const isOcrDoc = isOcrCandidate(document);
  const isJobActive = activeJob?.status === "Running" || activeJob?.status === "Pending" || activeJob?.status === "Paused";

  return (
    <div className="settings-card document-detail-card">
      <div className="settings-card__header">
        <h3 style={{ fontSize: "15px" }}>{document.originalFileName}</h3>
        <span className={getDocumentStatusClassName(document.status)}>
          {statusLabels[document.status]}
        </span>
      </div>

      {/* Pipeline visual */}
      {pipelineStatus && (
        <PipelineVisual status={pipelineStatus} />
      )}

      {/* Active job progress */}
      {activeJob && isJobActive && (
        <div className="document-job-progress">
          <div className="document-details__row">
            <span>{activeJob.currentStep || "Elaborazione in corso"}</span>
            <strong>{activeJob.progressPercent}%</strong>
          </div>
          <ProgressBar label={`Avanzamento ${activeJob.progressPercent}%`} value={activeJob.progressPercent} />
          {activeJob.error && <div className="job-error-message">{activeJob.error}</div>}
        </div>
      )}

      {/* Error */}
      {document.lastError && !isJobActive && (
        <div className="job-error-message">{document.lastError}</div>
      )}

      {/* Metadata */}
      <details className="document-meta-details">
        <summary>Dettagli tecnici</summary>
        <div className="document-details" style={{ marginTop: "10px" }}>
          <div className="document-details__row">
            <span>Dimensione</span>
            <strong>{formatFileSize(document.fileSizeBytes)}</strong>
          </div>
          <div className="document-details__row">
            <span>Importato il</span>
            <strong>{new Date(document.createdAtUtc).toLocaleString()}</strong>
          </div>
          <div className="document-details__row">
            <span>{formatIndexedUnitLabel(document)}</span>
            <strong>{document.pageCount > 0 ? document.pageCount : "Non note"}</strong>
          </div>
          <div className="document-details__row">
            <span>Chunk indicizzati</span>
            <strong>{document.chunkCount}</strong>
          </div>
          {embeddingStatus && (
            <div className="document-details__row">
              <span>Chunk embedded</span>
              <strong>{embeddingStatus.embeddedChunkCount}/{embeddingStatus.chunkCount}</strong>
            </div>
          )}
          {ocrStatus && ocrStatus.ocrPageCount > 0 && (
            <div className="document-details__row">
              <span>Pagine OCR</span>
              <strong>{ocrStatus.ocrPageCount}/{ocrStatus.pageCount}</strong>
            </div>
          )}
          <div className="document-details__row">
            <span>SHA-256</span>
            <strong className="document-details__hash">{document.sha256 ?? "Non disponibile"}</strong>
          </div>
        </div>
      </details>

      {/* Actions */}
      <div className="document-actions-grid">
        {canPreview && (
          <ActionButton
            label="Anteprima"
            tooltip="Visualizza il documento originale con testo estratto e metadati."
            disabled={isBusy}
            variant="primary"
            onClick={() => onPreview(document)}
          />
        )}

        {isOcrDoc && (
          <ActionButton
            label="Riesegui lettura testo"
            tooltip="Riprova l'OCR sulle pagine con errori o testo incompleto."
            disabled={isBusy || isJobActive}
            variant="recovery"
            onClick={() => onOcr(document, false)}
          />
        )}

        {isOcrDoc && (
          <ActionButton
            label="Rileggi tutto con OCR"
            tooltip="Rielabora tutte le pagine come immagini, anche se contengono testo. Invalida testo, indice e ricerca semantica esistenti."
            disabled={isBusy || isJobActive}
            variant="destructive"
            onClick={() => onOcr(document, true)}
          />
        )}

        <ActionButton
          label="Ricostruisci indice"
          tooltip="Ricrea testo, chunk e indice del documento. Utile se l'estrazione è incompleta o errata."
          disabled={isBusy || isJobActive}
          variant="recovery"
          onClick={() => onReindex(document)}
        />

        <ActionButton
          label="Rigenera ricerca semantica"
          tooltip={document.chunkCount === 0
            ? "Non ci sono chunk: usa prima \"Ricostruisci indice\"."
            : "Ricrea gli embeddings usati dalla ricerca semantica e dalla chat."}
          disabled={isBusy || isJobActive || document.chunkCount === 0}
          variant="recovery"
          onClick={() => onEmbed(document)}
        />

        <ActionButton
          label="Elimina documento"
          tooltip="Rimuove il documento e tutti i dati associati (testo, chunk, embeddings)."
          disabled={isBusy}
          variant="destructive"
          onClick={() => onDelete(document)}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Pipeline visual
// ---------------------------------------------------------------------------
type PhaseKey = "import" | "analysis" | "ocr" | "textExtraction" | "chunking" | "embedding";

function PipelineVisual({ status }: { status: DocumentPipelineStatus }) {
  const phases: PhaseKey[] = ["import", "analysis", "ocr", "textExtraction", "chunking", "embedding"];

  return (
    <div className="pipeline-visual" aria-label="Stato elaborazione documento">
      {phases.map((phase) => {
        const info = status[phase as keyof DocumentPipelineStatus] as { state: PhaseState; error: string | null } | undefined;
        if (!info || typeof info !== "object") return null;
        const phaseState = info.state;
        return (
          <div key={phase} className={`pipeline-phase pipeline-phase--${phaseState.toLowerCase()}`}>
            <span className="pipeline-phase__icon" aria-hidden="true">
              {phaseStateIcon[phaseState]}
            </span>
            <span className="pipeline-phase__label">{phaseLabels[phase] ?? phase}</span>
            {phaseState === "InProgress" && (
              <span className="pipeline-phase__progress-dot" aria-hidden="true" />
            )}
            {info.error && phaseState === "Failed" && (
              <span className="pipeline-phase__error" title={info.error}>⚠</span>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Action button
// ---------------------------------------------------------------------------
function ActionButton({
  label,
  tooltip,
  disabled,
  variant,
  onClick
}: {
  label: string;
  tooltip: string;
  disabled: boolean;
  variant: "primary" | "recovery" | "destructive";
  onClick: () => void;
}) {
  const className = variant === "destructive"
    ? "doc-action-btn doc-action-btn--destructive"
    : variant === "primary"
      ? "doc-action-btn doc-action-btn--primary"
      : "doc-action-btn doc-action-btn--recovery";

  return (
    <div className="doc-action-wrapper" title={tooltip}>
      <button
        className={className}
        type="button"
        disabled={disabled}
        onClick={onClick}
      >
        {label}
      </button>
      <span className="doc-action-tooltip">{tooltip}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
const statusLabels: Record<string, string> = {
  Imported: "Importato",
  Queued: "In coda",
  Processing: "In lavorazione",
  Indexed: "Pronto",
  RequiresAdditionalComponent: "Richiede componente",
  Failed: "Errore"
};

function getDocumentStatusClassName(status: string): string {
  return `job-status job-status--${status.toLowerCase()}`;
}

function isOcrCandidate(document: ImportedDocument): boolean {
  const ext = document.fileExtension?.toLowerCase() ?? "";
  return [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"].includes(ext);
}

function getDefaultOcrLanguage(languages: OcrLanguage[]): string {
  return languages.find((language) => language.isDefault)?.code
    ?? languages[0]?.code
    ?? DEFAULT_OCR_LANGUAGE;
}

function formatDocumentType(document: ImportedDocument): string {
  switch (document.fileExtension?.toLowerCase()) {
    case ".docx": return "Word Open XML";
    case ".doc": return "Word legacy";
    case ".xlsx": return "Excel Open XML";
    case ".xls": return "Excel legacy";
    case ".pptx": return "PowerPoint Open XML";
    case ".ppt": return "PowerPoint legacy";
    case ".pdf": return "PDF";
    case ".md": case ".markdown": return "Markdown";
    case ".txt": return "Testo";
    default: return document.mimeType ?? document.fileExtension ?? "Sconosciuto";
  }
}

function formatIndexedUnitLabel(document: ImportedDocument): string {
  switch (document.fileExtension?.toLowerCase()) {
    case ".docx": return "Sezioni logiche";
    case ".xlsx": return "Fogli indicizzati";
    case ".pptx": return "Slide indicizzate";
    default: return "Pagine";
  }
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}
