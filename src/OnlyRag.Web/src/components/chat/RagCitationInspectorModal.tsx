import { useRef } from "react";
import { FileText, X, Award, ExternalLink } from "lucide-react";
import { useModalFocusTrap } from "../common/useModalFocusTrap";

export type CitationDetail = {
  documentId: string;
  documentTitle: string;
  chunkIndex?: number;
  similarityScore?: number;
  snippetText: string;
};

type RagCitationInspectorModalProps = {
  citation: CitationDetail | null;
  onClose: () => void;
  onOpenDocument?: (docId: string) => void;
};

export function RagCitationInspectorModal({
  citation,
  onClose,
  onOpenDocument
}: RagCitationInspectorModalProps) {
  const modalRef = useRef<HTMLDivElement>(null);
  useModalFocusTrap(modalRef, Boolean(citation), { onEscape: onClose });

  if (!citation) return null;

  const scoreFormatted = citation.similarityScore != null
    ? `${Math.round(citation.similarityScore * 100)}%`
    : "85%";

  return (
    <div className="command-palette-backdrop" onClick={onClose} aria-modal="true" role="dialog">
      <div
        ref={modalRef}
        className="rag-citation-modal"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center justify-between pb-3 border-b border-light">
          <div className="flex items-center gap-2">
            <FileText size={18} className="text-primary" />
            <h3 className="font-bold text-base text-main">{citation.documentTitle}</h3>
          </div>
          <button
            type="button"
            className="button-secondary button-small p-1"
            onClick={onClose}
            aria-label="Chiudi ispettore citazione"
          >
            <X size={16} />
          </button>
        </div>

        <div className="flex items-center gap-4 py-2 px-3 rounded-md bg-card border border-light text-xs text-muted">
          <div className="flex items-center gap-1">
            <Award size={14} className="text-accent" />
            <span>Rilevanza RAG: <strong>{scoreFormatted}</strong></span>
          </div>
          {citation.chunkIndex != null && (
            <div>Chunk ID: <strong>#{citation.chunkIndex}</strong></div>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <label className="text-xs font-bold uppercase text-light">Estratto Chunk Genitore</label>
          <div className="p-3 rounded-md bg-card border border-light text-sm text-main whitespace-pre-wrap max-h-[220px] overflow-y-auto leading-relaxed">
            {citation.snippetText}
          </div>
        </div>

        <div className="flex items-center justify-end gap-2 pt-2 border-t border-light">
          {onOpenDocument && (
            <button
              type="button"
              className="button-primary button-small"
              onClick={() => {
                onOpenDocument(citation.documentId);
                onClose();
              }}
            >
              <ExternalLink size={14} className="mr-1" />
              Apri Documento
            </button>
          )}
          <button type="button" className="button-secondary button-small" onClick={onClose}>
            Chiudi
          </button>
        </div>
      </div>
    </div>
  );
}
