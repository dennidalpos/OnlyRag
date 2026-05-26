import type { KeyboardEvent } from "react";
import type { ImportedDocument } from "../api";
import { formatDateTime } from "../pollingStatus";
import {
  formatDocumentType,
  formatFileSize,
  formatIndexedUnitLabel,
  getDocumentStatusClassName,
  statusLabels
} from "./DocumentsSection.formatting";

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
      role="option"
      tabIndex={isBusy ? -1 : 0}
      aria-selected={isSelected}
      aria-disabled={isBusy}
      onClick={() => !isBusy && onSelect(document.id)}
      onKeyDown={handleKeyDown}
    >
      <div className="document-row__header">
        <div>
          <strong title={document.originalFileName}>{document.originalFileName}</strong>
          <span>{formatDocumentType(document)}</span>
        </div>
        <span className={getDocumentStatusClassName(document.status)}>
          {statusLabels[document.status]}
        </span>
      </div>
      <div className="document-row__meta">
        <span>{formatFileSize(document.fileSizeBytes)}</span>
        <span>{formatDateTime(document.createdAtUtc)}</span>
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
