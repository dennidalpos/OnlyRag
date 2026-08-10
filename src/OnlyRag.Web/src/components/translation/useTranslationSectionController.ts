import { useEffect, useMemo, useRef, useState } from "react";
import {
  apiRequest,
  type OllamaModel,
  type OllamaStatusResponse,
  type OperationMessageResponse,
  type TranslationCompare,
  type TranslationDetail,
  type TranslationExport,
  type TranslationSummary,
  type TranslationUnit
} from "../../api";
import { clearExitContributor, setExitContributor } from "../../appLifecycle";
import {
  getFirstAvailableCompareUnit,
  getPreferredTranslationModel
} from "./TranslationSection.controllerHelpers";
import { buildCompareDraftKey } from "./TranslationSection.helpers";
import {
  clearCompareDraft,
  loadCompareDraft,
  saveOrClearCompareDraft
} from "./TranslationSection.storage";
import type { ExportFormat, FeedbackState } from "./TranslationSection.types";
import { useTranslationLibraryController } from "./useTranslationLibraryController";
import { useModalFocusTrap } from "../common/useModalFocusTrap";

export function useTranslationSectionController({
  models,
  defaultModel,
  ollamaStatus
}: {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
}) {
  const [selectedLanguage, setSelectedLanguage] = useState("Italian");
  const [selectedModel, setSelectedModel] = useState("");
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
  const {
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
  } = useTranslationLibraryController(setFeedback);
  const compareDialogRef = useRef<HTMLDivElement | null>(null);
  const compareOpenerRef = useRef<HTMLElement | null>(null);
  const detailsPanelRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setSelectedModel(getPreferredTranslationModel(models, defaultModel));
  }, [defaultModel, models]);

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

  async function startTranslation(customGlossary?: Record<string, string>) {
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
          model: selectedModel,
          customGlossary: customGlossary && Object.keys(customGlossary).length > 0 ? customGlossary : undefined
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
      clearCompareDraft(compareDraftKey);

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
