import type { Dispatch, SetStateAction } from "react";
import {
  apiRequest,
  type IngestionSettings,
  type PdfExportSettings,
  type OcrProcessingSettings,
  type OcrSettings,
  type OllamaSettings,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../../api";
import {
  buildIngestionSettingsPayload,
  buildOcrProcessingSettingsPayload,
  buildOcrSettingsPayload,
  buildPdfExportSettingsPayload,
  buildOllamaSettingsPayload,
  buildPerformanceSettingsPayload,
  normalizeIngestionSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizePdfExportSettings,
  normalizeOllamaSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
import {
  emptyIngestionSettings,
  emptyOcrProcessingSettings,
  emptyOcrSettings,
  emptyPdfExportSettings,
  emptySettings,
  performanceProfilePresets
} from "./SettingsSection.defaults";

type SettingsSectionResetActionParams = {
  onDataChanged: () => Promise<void>;
  setFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setSavedFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setPdfExportFormState: Dispatch<SetStateAction<PdfExportSettings>>;
  setSavedPdfExportFormState: Dispatch<SetStateAction<PdfExportSettings>>;
  setPerformanceFormState: Dispatch<SetStateAction<PerformanceSettings>>;
  setSavedPerformanceFormState: Dispatch<SetStateAction<PerformanceSettings>>;
  setIngestionFormState: Dispatch<SetStateAction<IngestionSettings>>;
  setSavedIngestionFormState: Dispatch<SetStateAction<IngestionSettings>>;
  setOcrProcessingFormState: Dispatch<SetStateAction<OcrProcessingSettings>>;
  setSavedOcrProcessingFormState: Dispatch<SetStateAction<OcrProcessingSettings>>;
  setOcrFormState: Dispatch<SetStateAction<OcrSettings>>;
  setSavedOcrFormState: Dispatch<SetStateAction<OcrSettings>>;
  setInfoMessage: Dispatch<SetStateAction<string | null>>;
  setErrorMessage: Dispatch<SetStateAction<string | null>>;
  setIsBusy: Dispatch<SetStateAction<boolean>>;
  refreshPdfExportConverter: () => Promise<void>;
  refreshDependencyStatus: () => Promise<void>;
};

export function createSettingsSectionResetActions(params: SettingsSectionResetActionParams) {
  const {
    onDataChanged,
    setFormState,
    setSavedFormState,
    setPdfExportFormState,
    setSavedPdfExportFormState,
    setPerformanceFormState,
    setSavedPerformanceFormState,
    setIngestionFormState,
    setSavedIngestionFormState,
    setOcrProcessingFormState,
    setSavedOcrProcessingFormState,
    setOcrFormState,
    setSavedOcrFormState,
    setInfoMessage,
    setErrorMessage,
    setIsBusy,
    refreshPdfExportConverter,
    refreshDependencyStatus
  } = params;

  async function restoreBalancedDefaults() {
    if (!window.confirm("Ripristinare le impostazioni iniziali bilanciate senza eliminare documenti e dati locali?")) {
      return;
    }

    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const defaultPerformance = performanceProfilePresets.balanced;
      const [
        savedPerformance,
        savedOllama,
        savedPdfExport,
        savedIngestion,
        savedOcrProcessing,
        savedOcr
      ] = await Promise.all([
        apiRequest<PerformanceSettings>("/api/settings/performance", {
          method: "PUT",
          body: JSON.stringify(buildPerformanceSettingsPayload(defaultPerformance))
        }),
        apiRequest<OllamaSettings>("/api/settings/ollama", {
          method: "PUT",
          body: JSON.stringify(buildOllamaSettingsPayload(emptySettings, defaultPerformance))
        }),
        apiRequest<PdfExportSettings>("/api/settings/pdf-export", {
          method: "PUT",
          body: JSON.stringify(buildPdfExportSettingsPayload(emptyPdfExportSettings))
        }),
        apiRequest<IngestionSettings>("/api/settings/ingestion", {
          method: "PUT",
          body: JSON.stringify(buildIngestionSettingsPayload(emptyIngestionSettings))
        }),
        apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing", {
          method: "PUT",
          body: JSON.stringify(buildOcrProcessingSettingsPayload(emptyOcrProcessingSettings))
        }),
        apiRequest<OcrSettings>("/api/settings/ocr", {
          method: "PUT",
          body: JSON.stringify(buildOcrSettingsPayload(emptyOcrSettings))
        })
      ]);

      const normalizedPerformance = normalizePerformanceSettings(savedPerformance);
      const normalizedOllama = normalizeOllamaSettings(savedOllama);
      const normalizedPdfExport = normalizePdfExportSettings(savedPdfExport);
      const normalizedIngestion = normalizeIngestionSettings(savedIngestion);
      const normalizedOcrProcessing = normalizeOcrProcessingSettings(savedOcrProcessing);
      const normalizedOcr = normalizeOcrSettings(savedOcr);

      setPerformanceFormState(normalizedPerformance);
      setSavedPerformanceFormState(normalizedPerformance);
      setFormState(normalizedOllama);
      setSavedFormState(normalizedOllama);
      setPdfExportFormState(normalizedPdfExport);
      setSavedPdfExportFormState(normalizedPdfExport);
      setIngestionFormState(normalizedIngestion);
      setSavedIngestionFormState(normalizedIngestion);
      setOcrProcessingFormState(normalizedOcrProcessing);
      setSavedOcrProcessingFormState(normalizedOcrProcessing);
      setOcrFormState(normalizedOcr);
      setSavedOcrFormState(normalizedOcr);
      setInfoMessage("Impostazioni iniziali bilanciate ripristinate. I dati locali non sono stati eliminati.");
      await refreshPdfExportConverter();
      await refreshDependencyStatus();
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Ripristino impostazioni non riuscito.");
    } finally {
      setIsBusy(false);
    }
  }

  async function requestAppDataReset() {
    if (!window.confirm("Pianificare il reset totale al prossimo avvio? Verranno eliminati documenti, indici, chat, cache, log, profilo WebView2 e impostazioni locali.")) {
      return;
    }

    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>("/api/app/reset-on-next-startup", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Reset dati non pianificato.");
    } finally {
      setIsBusy(false);
    }
  }

  return {
    restoreBalancedDefaults,
    requestAppDataReset
  } as const;
}
