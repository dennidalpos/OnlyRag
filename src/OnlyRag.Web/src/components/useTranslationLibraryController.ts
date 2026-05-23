import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from "react";
import {
  apiRequest,
  type ImportedDocument,
  type TranslationDetail,
  type TranslationSummary
} from "../api";
import type { FeedbackState } from "./TranslationSection.types";

export function useTranslationLibraryController(
  setFeedback: Dispatch<SetStateAction<FeedbackState>>
) {
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);
  const [selectedDocumentId, setSelectedDocumentId] = useState<number | null>(null);
  const [translations, setTranslations] = useState<TranslationSummary[]>([]);
  const [selectedTranslationId, setSelectedTranslationId] = useState<number | null>(null);
  const [selectedTranslation, setSelectedTranslation] = useState<TranslationDetail | null>(null);

  const selectedDocument = useMemo(
    () => documents.find((document) => document.id === selectedDocumentId) ?? null,
    [documents, selectedDocumentId]
  );

  useEffect(() => {
    let isCancelled = false;

    async function loadDocuments() {
      try {
        const docs = await apiRequest<ImportedDocument[]>("/api/documents");
        if (isCancelled) {
          return;
        }

        setDocuments(docs);
        setSelectedDocumentId((current) => current ?? docs[0]?.id ?? null);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({
            tone: "error",
            message: error instanceof Error ? error.message : "Impossibile leggere i documenti."
          });
        }
      }
    }

    void loadDocuments();

    return () => {
      isCancelled = true;
    };
  }, [setFeedback]);

  useEffect(() => {
    let isCancelled = false;

    async function refreshTranslations() {
      if (!selectedDocumentId) {
        setTranslations([]);
        setSelectedTranslation(null);
        return;
      }

      try {
        const items = await apiRequest<TranslationSummary[]>(
          `/api/documents/${selectedDocumentId}/translations`
        );
        if (isCancelled) {
          return;
        }

        setTranslations(items);
        setSelectedTranslationId((current) => current ?? items[0]?.id ?? null);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({
            tone: "error",
            message: error instanceof Error ? error.message : "Impossibile leggere le traduzioni."
          });
        }
      }
    }

    void refreshTranslations();
    const interval = window.setInterval(() => void refreshTranslations(), 3000);

    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocumentId, setFeedback]);

  useEffect(() => {
    let isCancelled = false;

    async function refreshTranslationDetail() {
      if (!selectedTranslationId) {
        setSelectedTranslation(null);
        return;
      }

      try {
        const detail = await apiRequest<TranslationDetail>(`/api/translations/${selectedTranslationId}`);
        if (!isCancelled) {
          setSelectedTranslation(detail);
        }
      } catch {
        if (!isCancelled) {
          setSelectedTranslation(null);
        }
      }
    }

    void refreshTranslationDetail();
    const interval = window.setInterval(() => void refreshTranslationDetail(), 3000);

    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedTranslationId]);

  function handleDocumentChange(documentId: number | null) {
    setSelectedDocumentId(documentId);
    setSelectedTranslationId(null);
    setSelectedTranslation(null);
  }

  return {
    documents,
    handleDocumentChange,
    selectedDocument,
    selectedDocumentId,
    selectedTranslation,
    selectedTranslationId,
    setSelectedTranslation,
    setSelectedTranslationId,
    setTranslations,
    translations
  };
}
