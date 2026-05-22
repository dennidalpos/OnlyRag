import { useState } from "react";
import { apiRequest, type DocumentPreviewResponse, type ImportedDocument } from "../api";

export function useDocumentPreviewController() {
  const [previewDocument, setPreviewDocument] = useState<ImportedDocument | null>(null);
  const [previewData, setPreviewData] = useState<DocumentPreviewResponse | null>(null);
  const [isLoadingPreview, setIsLoadingPreview] = useState(false);

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

  return {
    handleClosePreview,
    handleOpenPreview,
    isLoadingPreview,
    loadPreviewPage,
    previewData,
    previewDocument
  };
}
