import { useRef, useState } from "react";
import type { DocumentPageInfo, DocumentPreviewResponse, ImportedDocument } from "../../api";
import { formatFileSize, statusLabels } from "./DocumentsSection.formatting";
import { useModalMaximize } from "../common/useModalMaximize";
import { useModalFocusTrap } from "../common/useModalFocusTrap";

type Props = {
  document: ImportedDocument;
  preview: DocumentPreviewResponse | null;
  isLoading: boolean;
  onClose: () => void;
  onPageChange: (page: number) => void;
};

export function DocumentPreviewModal({ document, preview, isLoading, onClose, onPageChange }: Props) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const selectedPage = preview?.pageStart ?? 1;
  const pages = preview?.pages ?? [];
  const currentPage = pages.find((p) => p.pageNumber === selectedPage) ?? pages[0] ?? null;
  const totalPages = preview?.pageCount ?? document.pageCount;
  const pageNumbers = buildVisiblePageNumbers(selectedPage, totalPages);
  const modalSize = useModalMaximize();
  useModalFocusTrap(modalRef, true, { onEscape: onClose });

  function handleBackdropClick(e: React.MouseEvent<HTMLDivElement>) {
    if (e.target === e.currentTarget) onClose();
  }

  return (
    <div className="modal-backdrop" onClick={handleBackdropClick}>
      <div
        className={`preview-modal modal-frame--resizable${modalSize.maximizedClassName}`}
        role="dialog"
        aria-modal="true"
        aria-label="Anteprima documento"
        ref={modalRef}
        tabIndex={-1}
      >
        <div className="preview-modal__header">
          <div>
            <h3>{document.originalFileName}</h3>
            <span>
              {preview?.pageCount
                ? `${preview.pageCount} ${preview.fileExtension?.toLowerCase() === ".xlsx" ? "fogli" : preview.fileExtension?.toLowerCase() === ".pptx" ? "slide" : "pagine"}`
                : "Caricamento..."}
              {" · "}
              {formatFileSize(document.fileSizeBytes)}
              {preview?.mimeType && ` · ${preview.mimeType}`}
            </span>
          </div>
          <div className="compare-header-actions">
            <button className="button-secondary" type="button" onClick={modalSize.toggleMaximized}>
              {modalSize.maximizeLabel}
            </button>
            <button className="button-secondary" type="button" onClick={onClose} aria-label="Chiudi anteprima">
              Chiudi
            </button>
          </div>
        </div>

        {isLoading && (
          <div className="preview-modal__loading" role="status" aria-live="polite">
            <p>Caricamento anteprima in corso...</p>
          </div>
        )}

        {!isLoading && (!preview || pages.length === 0) && (
          <div className="preview-modal__error" role="alert">
            <p>
              {!preview
                ? "Anteprima non disponibile. Il documento potrebbe essere in fase di elaborazione o non ancora indicizzato."
                : "Nessuna pagina disponibile per questo documento."}
            </p>
          </div>
        )}

        {!isLoading && preview && pages.length > 0 && (
          <>
            {/* Metadata strip */}
            <div className="preview-meta-strip">
              <span><strong>Formato:</strong> {preview.fileExtension?.toUpperCase() ?? "N/D"}</span>
              <span><strong>Dimensione:</strong> {formatFileSize(preview.fileSizeBytes)}</span>
              <span><strong>Pagine/sezioni:</strong> {preview.pageCount}</span>
              <span><strong>Chunk:</strong> {preview.chunkCount}</span>
              <span><strong>Stato:</strong> {formatDocumentPreviewStatus(preview.status)}</span>
            </div>

            <div className="preview-modal__body">
              {/* Page selector sidebar */}
              {pageNumbers.length > 1 && (
                <div className="preview-page-list">
                  <div className="preview-page-list__header">Pagine</div>
                  {pageNumbers.map((pn) => {
                    const pg = pages.find((p) => p.pageNumber === pn)!;
                    return (
                      <button
                        key={pn}
                        className={pn === selectedPage ? "preview-page-btn preview-page-btn--active" : "preview-page-btn"}
                        type="button"
                        aria-current={pn === selectedPage ? "page" : undefined}
                        aria-label={`Vai alla pagina ${pn}`}
                        onClick={() => onPageChange(pn)}
                      >
                        <span>{pn}</span>
                        {pg && <PageOcrBadge page={pg} />}
                      </button>
                    );
                  })}
                </div>
              )}

              {/* Page content */}
              <div className="preview-page-content">
                {currentPage ? (
                  <PageContent page={currentPage} />
                ) : (
                  <div className="preview-no-pages" role="status">
                    <p>Nessuna pagina disponibile per la selezione corrente.</p>
                  </div>
                )}
              </div>
            </div>

            {/* Page navigation */}
            {pageNumbers.length > 1 && (
              <div className="preview-page-nav">
                <button
                  className="button-secondary"
                  type="button"
                  disabled={selectedPage <= 1 || isLoading}
                  onClick={() => onPageChange(selectedPage - 1)}
                >
                  ← Precedente
                </button>
                <span>Pagina {selectedPage} di {totalPages}</span>
                <button
                  className="button-secondary"
                  type="button"
                  disabled={selectedPage >= totalPages || isLoading}
                  onClick={() => onPageChange(selectedPage + 1)}
                >
                  Successiva →
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function buildVisiblePageNumbers(selectedPage: number, totalPages: number): number[] {
  if (totalPages <= 0) return [];

  const visibleCount = Math.min(totalPages, 11);
  const halfWindow = Math.floor(visibleCount / 2);
  const start = Math.max(1, Math.min(selectedPage - halfWindow, totalPages - visibleCount + 1));
  return Array.from({ length: visibleCount }, (_, index) => start + index);
}

function PageContent({ page }: { page: DocumentPageInfo }) {
  const [copied, setCopied] = useState(false);

  function handleCopy() {
    if (page.textContent) {
      navigator.clipboard.writeText(page.textContent).then(() => {
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
      }).catch(() => {});
    }
  }

  return (
    <div className="preview-page-view">
      <div className="preview-page-view__header">
        <span className="preview-page-title">Pagina {page.pageNumber}</span>
        <div className="preview-page-actions">
          <PageOcrBadge page={page} />
          {page.textContent && (
            <button className="button-secondary preview-copy-button" type="button" onClick={handleCopy}>
              {copied ? "Copiato!" : "Copia testo"}
            </button>
          )}
        </div>
      </div>

      {page.ocrError && (
        <div className="job-error-message" role="alert" style={{ marginBottom: "10px" }}>
          Errore OCR pagina: {page.ocrError}
        </div>
      )}

      {page.textContent ? (
        <div className="preview-page-text">
          <pre>{page.textContent}</pre>
        </div>
      ) : (
        <div className="preview-no-text" role="status">
          <p>Nessun testo estratto per questa pagina.</p>
          {page.ocrStatus && <p>Stato OCR: {formatPageOcrStatus(page.ocrStatus)}</p>}
        </div>
      )}
    </div>
  );
}

function PageOcrBadge({ page }: { page: DocumentPageInfo }) {
  if (!page.ocrStatus) return null;
  const isOk = page.ocrStatus === "Complete" || page.ocrStatus === "Cached" || page.ocrStatus === "LowConfidence";
  const isError = page.ocrStatus === "Failed" || page.ocrStatus === "Error";
  const className = isError
    ? "page-ocr-badge page-ocr-badge--error"
    : isOk
      ? "page-ocr-badge page-ocr-badge--ok"
      : "page-ocr-badge";
  const label = isError
    ? "Errore OCR"
    : isOk
      ? (page.ocrConfidence != null ? `OCR ${Math.round(page.ocrConfidence * 100)}%` : "OCR completato")
      : formatPageOcrStatus(page.ocrStatus);
  return <span className={className}>{label}</span>;
}

function formatDocumentPreviewStatus(status: string): string {
  return statusLabels[status] ?? status;
}

function formatPageOcrStatus(status: string): string {
  switch (status) {
    case "Complete":
    case "Cached":
      return "Completato";
    case "LowConfidence":
      return "Bassa confidenza";
    case "Failed":
    case "Error":
      return "Errore";
    case "Pending":
      return "In attesa";
    case "Running":
      return "In corso";
    case "Skipped":
      return "Saltato";
    default:
      return status;
  }
}
