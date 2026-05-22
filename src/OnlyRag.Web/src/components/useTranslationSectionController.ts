import { useEffect, useMemo, useRef, useState } from "react";
import {
  apiRequest,
  type ImportedDocument,
  type OllamaModel,
  type OllamaStatusResponse,
  type OperationMessageResponse,
  type TranslationCompare,
  type TranslationDetail,
  type TranslationExport,
  type TranslationSummary,
  type TranslationUnit
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import {
  getFirstAvailableCompareUnit,
  getPreferredTranslationModel,
  loadCompareDraft,
  saveOrClearCompareDraft
} from "./TranslationSection.controllerHelpers";
import { buildCompareDraftKey } from "./TranslationSection.helpers";
import type { ExportFormat, FeedbackState } from "./TranslationSection.types";
import { useModalFocusTrap } from "./useModalFocusTrap";

export function useTranslationSectionController({
  models,
  defaultModel,
  ollamaStatus
}: {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
}) {
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);
  const [selectedDocumentId, setSelectedDocumentId] = useState<number | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState("English");
  const [selectedModel, setSelectedModel] = useState("");
  const [translations, setTranslations] = useState<TranslationSummary[]>([]);
  const [selectedTranslationId, setSelectedTranslationId] = useState<number | null>(null);
  const [selectedTranslation, setSelectedTranslation] = useState<TranslationDetail | null>(null);
  const [compareTranslationId, setCompareTranslationId] = useState<number | null>(null);
  const [comparePage, setComparePage] = useState<number | null>(null);
  const [compareData, setCompareData] = useState<TranslationCompare | null>(null);
  const [activeCompareUnitId, setActiveCompareUnitId] = useState<number | null>(null);
  const [editedTranslationText, setEditedTranslationText] = useState("");
  const [isCompareLoading, setIsCompareLoading] = useState(false);
  const [saveState, setSaveState] = useState<FeedbackState>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [exportFormat, setExportFormat] = useState<ExportFormat>("markdown");
  const [isExporting, setIsExporting] = useState(false);
  const [lastExportPath, setLastExportPath] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<FeedbackState>(null);
  const compareDialogRef = useRef<HTMLDivElement | null>(null);
  const compareOpenerRef = useRef<HTMLElement | null>(null);
  const detailsPanelRef = useRef<HTMLDivElement | null>(null);

  const selectedDocument = useMemo(
    () => documents.find((document) => document.id === selectedDocumentId) ?? null,
    [documents, selectedDocumentId]
  );

  useEffect(() => {
    setSelectedModel(getPreferredTranslationModel(models, defaultModel));
  }, [defaultModel, models]);

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
  }, []);

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
  }, [selectedDocumentId]);

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

  useEffect(() => {
    let isCancelled = false;

    async function loadCompare() {
      if (!compareTranslationId) {
        setCompareData(null);
        setActiveCompareUnitId(null);
        setEditedTranslationText("");
        setSaveState(null);
        return;
      }

      setIsCompareLoading(true);
      try {
        const suffix = comparePage ? `?page=${comparePage}` : "";
        const data = await apiRequest<TranslationCompare>(
          `/api/translations/${compareTranslationId}/compare${suffix}`
        );
        if (isCancelled) {
          return;
        }

        const firstAvailableUnit = getFirstAvailableCompareUnit(data);
        setCompareData(data);
        setComparePage(data.currentPage);
        setActiveCompareUnitId((current) =>
          current && data.units.some((unit) => unit.id === current)
            ? current
            : firstAvailableUnit?.id ?? null
        );
      } catch (error) {
        if (!isCancelled) {
          setSaveState({
            tone: "error",
            message: error instanceof Error ? error.message : "Confronto non disponibile."
          });
        }
      } finally {
        if (!isCancelled) {
          setIsCompareLoading(false);
        }
      }
    }

    void loadCompare();

    return () => {
      isCancelled = true;
    };
  }, [comparePage, compareTranslationId]);

  const activeCompareUnit = useMemo(() => {
    return compareData?.units.find((unit) => unit.id === activeCompareUnitId) ?? null;
  }, [activeCompareUnitId, compareData]);

  const compareDraftKey = activeCompareUnit
    ? buildCompareDraftKey(compareTranslationId, activeCompareUnit.id)
    : null;

  const hasUnsavedCompareDraft = Boolean(
    activeCompareUnit && editedTranslationText !== (activeCompareUnit.translatedText ?? "")
  );

  useEffect(() => {
    if (!activeCompareUnit) {
      setEditedTranslationText("");
      setSaveState(null);
      return;
    }

    const savedDraft = loadCompareDraft(compareDraftKey);
    setEditedTranslationText(savedDraft ?? activeCompareUnit.translatedText ?? "");

    setSaveState(null);
  }, [activeCompareUnit, compareDraftKey]);

  useEffect(() => {
    saveOrClearCompareDraft(compareDraftKey, hasUnsavedCompareDraft, editedTranslationText);
  }, [compareDraftKey, editedTranslationText, hasUnsavedCompareDraft]);

  useEffect(() => {
    setExitContributor("translation", {
      label: "Traduzione",
      hasPendingChanges: hasUnsavedCompareDraft,
      hasActiveWork: isStarting || isExporting || isCompareLoading,
      prepareForExit: persistCompareDraftAsync
    });

    return () => {
      clearExitContributor("translation");
    };
  }, [hasUnsavedCompareDraft, isCompareLoading, isExporting, isStarting, persistCompareDraftAsync]);

  useModalFocusTrap(compareDialogRef, Boolean(compareTranslationId), {
    onEscape: closeCompare,
    restoreFocus: false
  });

  async function startTranslation() {
    if (!selectedDocumentId) {
      setFeedback({ tone: "error", message: "Seleziona un documento." });
      return;
    }

    if (!selectedModel) {
      setFeedback({ tone: "error", message: "Seleziona un modello Ollama installato." });
      return;
    }

    setIsStarting(true);
    setFeedback(null);
    try {
      const detail = await apiRequest<TranslationDetail>("/api/translations", {
        method: "POST",
        body: JSON.stringify({
          documentId: selectedDocumentId,
          targetLanguage: selectedLanguage,
          model: selectedModel
        })
      });
      setSelectedTranslationId(detail.translation.id);
      setSelectedTranslation(detail);
      const items = await apiRequest<TranslationSummary[]>(
        `/api/documents/${selectedDocumentId}/translations`
      );
      setTranslations(items);
      setFeedback({ tone: "info", message: "Traduzione accodata." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Traduzione non avviata."
      });
    } finally {
      setIsStarting(false);
    }
  }

  async function exportTranslation() {
    if (!selectedTranslationId) {
      return;
    }

    setIsExporting(true);
    setFeedback(null);
    try {
      const exported = await apiRequest<TranslationExport>(
        `/api/translations/${selectedTranslationId}/export`,
        {
          method: "POST",
          body: JSON.stringify({ format: exportFormat })
        }
      );
      setLastExportPath(exported.outputPath);
      setFeedback({ tone: "info", message: `Export completato: ${exported.outputPath}` });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Export non riuscito."
      });
    } finally {
      setIsExporting(false);
    }
  }

  async function openExportFolder() {
    try {
      await apiRequest<OperationMessageResponse>("/api/documents/exports/open-folder", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Cartella export non aperta."
      });
    }
  }

  function openCompare(translationId: number, page?: number | null) {
    compareOpenerRef.current = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    setCompareTranslationId(translationId);
    setComparePage(page ?? null);
    setCompareData(null);
    setActiveCompareUnitId(null);
    setEditedTranslationText("");
    setSaveState(null);
  }

  function closeCompare() {
    setCompareTranslationId(null);
    setComparePage(null);
    setCompareData(null);
    setActiveCompareUnitId(null);
    setEditedTranslationText("");
    setSaveState(null);
    compareOpenerRef.current?.focus();
    compareOpenerRef.current = null;
  }

  async function persistCompareDraftAsync() {
    if (!activeCompareUnit) {
      return;
    }

    saveOrClearCompareDraft(compareDraftKey, hasUnsavedCompareDraft, editedTranslationText);

    if (!hasUnsavedCompareDraft || !editedTranslationText.trim()) {
      return;
    }

    await saveCorrection(true);
  }

  async function saveCorrection(isSilent = false) {
    if (!compareTranslationId || !activeCompareUnit) {
      return;
    }

    if (!editedTranslationText.trim()) {
      if (!isSilent) {
        setSaveState({ tone: "error", message: "Inserisci il testo tradotto." });
      }
      return;
    }

    if (!isSilent) {
      setSaveState({ tone: "info", message: "Salvataggio..." });
    }
    try {
      const updated = await apiRequest<TranslationUnit>(
        `/api/translations/${compareTranslationId}/units/${activeCompareUnit.id}`,
        {
          method: "PUT",
          body: JSON.stringify({ translatedText: editedTranslationText })
        }
      );
      setCompareData((current) =>
        current
          ? {
              ...current,
              units: current.units.map((unit) => (unit.id === updated.id ? updated : unit))
            }
          : current
      );
      setSelectedTranslation((current) =>
        current && current.translation.id === compareTranslationId
          ? {
              ...current,
              units: current.units.map((unit) => (unit.id === updated.id ? updated : unit))
            }
          : current
      );
      if (compareDraftKey) {
        try {
          window.localStorage.removeItem(compareDraftKey);
        } catch {
        }
      }

      if (!isSilent) {
        setSaveState({ tone: "info", message: "Correzione salvata." });
      }
    } catch (error) {
      if (!isSilent) {
        setSaveState({
          tone: "error",
          message: error instanceof Error ? error.message : "Salvataggio non riuscito."
        });
      }

      throw error;
    }
  }

  const canStart =
    Boolean(ollamaStatus?.isReachable)
    && models.length > 0
    && Boolean(selectedDocumentId)
    && Boolean(selectedModel)
    && !isStarting;

  function handleDocumentChange(documentId: number | null) {
    setSelectedDocumentId(documentId);
    setSelectedTranslationId(null);
    setSelectedTranslation(null);
  }

  return {
    activeCompareUnit,
    activeCompareUnitId,
    canStart,
    closeCompare,
    compareData,
    compareDialogRef,
    compareTranslationId,
    detailsPanelRef,
    documents,
    editedTranslationText,
    exportFormat,
    exportTranslation,
    feedback,
    handleDocumentChange,
    isCompareLoading,
    isExporting,
    isStarting,
    lastExportPath,
    openCompare,
    openExportFolder,
    saveCorrection,
    saveState,
    selectedDocument,
    selectedDocumentId,
    selectedLanguage,
    selectedModel,
    selectedTranslation,
    selectedTranslationId,
    setActiveCompareUnitId,
    setComparePage,
    setEditedTranslationText,
    setExportFormat,
    setSelectedLanguage,
    setSelectedModel,
    setSelectedTranslationId,
    startTranslation,
    translations
  };
}
