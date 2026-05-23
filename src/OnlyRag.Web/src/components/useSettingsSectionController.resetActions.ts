import type { Dispatch, SetStateAction } from "react";
import {
  apiRequest,
  type IngestionSettings,
  type OfficeConversionSettings,
  type OcrProcessingSettings,
  type OcrSettings,
  type OllamaSettings,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../api";
import {
  buildIngestionSettingsPayload,
  buildOcrProcessingSettingsPayload,
  buildOcrSettingsPayload,
  buildOfficeSettingsPayload,
  buildOllamaSettingsPayload,
  buildPerformanceSettingsPayload,
  normalizeIngestionSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizeOfficeSettings,
  normalizeOllamaSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
import {
  emptyIngestionSettings,
  emptyOcrProcessingSettings,
  emptyOcrSettings,
  emptyOfficeSettings,
  emptySettings,
  performanceProfilePresets
} from "./SettingsSection.defaults";

type SettingsSectionResetActionParams = {
  onDataChanged: () => Promise<void>;
  setFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setSavedFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setOfficeFormState: Dispatch<SetStateAction<OfficeConversionSettings>>;
  setSavedOfficeFormState: Dispatch<SetStateAction<OfficeConversionSettings>>;
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
  refreshOfficeConverter: () => Promise<void>;
  refreshDependencyStatus: () => Promise<void>;
};

export function createSettingsSectionResetActions(params: SettingsSectionResetActionParams) {
  const {
    onDataChanged,
    setFormState,
    setSavedFormState,
    setOfficeFormState,
    setSavedOfficeFormState,
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
    refreshOfficeConverter,
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
        savedOffice,
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
        apiRequest<OfficeConversionSettings>("/api/settings/office-conversion", {
          method: "PUT",
          body: JSON.stringify(buildOfficeSettingsPayload(emptyOfficeSettings))
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
      const normalizedOffice = normalizeOfficeSettings(savedOffice);
      const normalizedIngestion = normalizeIngestionSettings(savedIngestion);
      const normalizedOcrProcessing = normalizeOcrProcessingSettings(savedOcrProcessing);
      const normalizedOcr = normalizeOcrSettings(savedOcr);

      setPerformanceFormState(normalizedPerformance);
      setSavedPerformanceFormState(normalizedPerformance);
      setFormState(normalizedOllama);
      setSavedFormState(normalizedOllama);
      setOfficeFormState(normalizedOffice);
      setSavedOfficeFormState(normalizedOffice);
      setIngestionFormState(normalizedIngestion);
      setSavedIngestionFormState(normalizedIngestion);
      setOcrProcessingFormState(normalizedOcrProcessing);
      setSavedOcrProcessingFormState(normalizedOcrProcessing);
      setOcrFormState(normalizedOcr);
      setSavedOcrFormState(normalizedOcr);
      setInfoMessage("Impostazioni iniziali bilanciate ripristinate. I dati locali non sono stati eliminati.");
      await refreshOfficeConverter();
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
