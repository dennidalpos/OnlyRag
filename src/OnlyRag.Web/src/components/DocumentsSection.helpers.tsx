import { useRef, useState, type KeyboardEvent } from "react";
import {
  type DocumentEmbeddingStatus,
  type DocumentOcrStatus,
  type DocumentPipelineStatus,
  type ImportedDocument,
  type LocalJob,
  type OcrLanguage,
  type OcrPolicy,
  type PhaseState
} from "../api";
import { ProgressBar } from "./ProgressBar";
import { useModalFocusTrap } from "./useModalFocusTrap";

const DEFAULT_OCR_LANGUAGE = "it";

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

const phaseStateLabels: Record<PhaseState, string> = {
  Completed: "completato",
  InProgress: "in corso",
  Failed: "errore",
  Skipped: "saltato",
  Todo: "da fare",
  Obsolete: "da aggiornare"
};
// ---------------------------------------------------------------------------
// OCR choice dialog
// ---------------------------------------------------------------------------
export function OcrChoiceDialog({
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

export function OcrLanguageDialog({
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

export function OcrLanguageSelect({
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
export function DocumentListRow({
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
export function DocumentDetailCard({
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
  const isJobActive = activeJob?.status === "Running" || activeJob?.status === "Pausing" || activeJob?.status === "Pending" || activeJob?.status === "Paused";

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

export function PipelineVisual({ status }: { status: DocumentPipelineStatus }) {
  const phases: PhaseKey[] = ["import", "analysis", "ocr", "textExtraction", "chunking", "embedding"];
  const hasActivePhase = phases.some((phase) => {
    const info = status[phase as keyof DocumentPipelineStatus] as { state: PhaseState } | undefined;
    return info?.state === "InProgress";
  });

  return (
    <div
      className="pipeline-visual"
      role="list"
      aria-label="Stato elaborazione documento"
      aria-live={hasActivePhase ? "polite" : undefined}
    >
      {phases.map((phase) => {
        const info = status[phase as keyof DocumentPipelineStatus] as { state: PhaseState; error: string | null } | undefined;
        if (!info || typeof info !== "object") return null;
        const phaseState = info.state;
        const phaseLabel = phaseLabels[phase] ?? phase;
        const stateLabel = phaseStateLabels[phaseState];
        return (
          <div
            key={phase}
            className={`pipeline-phase pipeline-phase--${phaseState.toLowerCase()}`}
            role="listitem"
            aria-label={`${phaseLabel}: ${stateLabel}${info.error ? `. ${info.error}` : ""}`}
          >
            <span className="pipeline-phase__icon" aria-hidden="true">
              {phaseStateIcon[phaseState]}
            </span>
            <span className="pipeline-phase__label">
              {phaseLabel}
              <span className="sr-only">: {stateLabel}</span>
            </span>
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
export function ActionButton({
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

export function getDocumentStatusClassName(status: string): string {
  return `job-status job-status--${status.toLowerCase()}`;
}

export function isOcrCandidate(document: ImportedDocument): boolean {
  const ext = document.fileExtension?.toLowerCase() ?? "";
  return [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"].includes(ext);
}

export function getDefaultOcrLanguage(languages: OcrLanguage[]): string {
  return languages.find((language) => language.isDefault)?.code
    ?? languages[0]?.code
    ?? DEFAULT_OCR_LANGUAGE;
}

export function formatDocumentType(document: ImportedDocument): string {
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

export function formatIndexedUnitLabel(document: ImportedDocument): string {
  switch (document.fileExtension?.toLowerCase()) {
    case ".docx": return "Sezioni logiche";
    case ".xlsx": return "Fogli indicizzati";
    case ".pptx": return "Slide indicizzate";
    default: return "Pagine";
  }
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}




