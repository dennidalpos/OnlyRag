import { useId } from "react";
import type {
  DocumentEmbeddingStatus,
  DocumentOcrStatus,
  DocumentPipelineStatus,
  ImportedDocument,
  LocalJob,
  PhaseState
} from "../api";
import { isActiveJobStatus } from "../jobStatus";
import { formatDateTime } from "../pollingStatus";
import {
  formatFileSize,
  formatIndexedUnitLabel,
  getDocumentStatusClassName,
  isOcrCandidate,
  statusLabels
} from "./DocumentsSection.formatting";
import { ProgressBar } from "./ProgressBar";

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
  const isJobActive = activeJob ? isActiveJobStatus(activeJob.status) : false;

  return (
    <div className="settings-card document-detail-card">
      <div className="settings-card__header">
        <h3 style={{ fontSize: "15px" }}>{document.originalFileName}</h3>
        <span className={getDocumentStatusClassName(document.status)}>
          {statusLabels[document.status]}
        </span>
      </div>

      {pipelineStatus && (
        <PipelineVisual status={pipelineStatus} />
      )}

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

      {document.lastError && !isJobActive && (
        <div className="job-error-message">{document.lastError}</div>
      )}

      <details className="document-meta-details">
        <summary>Dettagli tecnici</summary>
        <div className="document-details" style={{ marginTop: "10px" }}>
          <div className="document-details__row">
            <span>Dimensione</span>
            <strong>{formatFileSize(document.fileSizeBytes)}</strong>
          </div>
          <div className="document-details__row">
            <span>Importato il</span>
            <strong>{formatDateTime(document.createdAtUtc)}</strong>
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
  const descriptionId = useId();
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
        aria-describedby={descriptionId}
        onClick={onClick}
      >
        {label}
      </button>
      <span className="doc-action-tooltip" id={descriptionId}>{tooltip}</span>
    </div>
  );
}
