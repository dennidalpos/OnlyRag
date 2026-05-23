import {
  apiRequest,
  type IngestionSettings,
  type OfficeConversionSettings,
  type OcrProcessingSettings,
  type OcrSettings,
  type OllamaSettings,
  type OllamaStatusResponse,
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
  normalizeOfficeSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizeOllamaSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
import { getOcrProfilePreset } from "./SettingsSection.defaults";
import {
  createSettingsSectionDependencyActions,
  type SettingsSectionDependencyActionParams
} from "./useSettingsSectionController.dependencyActions";
import { createSettingsSectionResetActions } from "./useSettingsSectionController.resetActions";
import type { Dispatch, SetStateAction } from "react";

type SettingsSectionActionParams = SettingsSectionDependencyActionParams & {
  onDataChanged: () => Promise<void>;
  modelToInstall: string;
  formState: OllamaSettings;
  officeFormState: OfficeConversionSettings;
  performanceFormState: PerformanceSettings;
  ingestionFormState: IngestionSettings;
  ocrProcessingFormState: OcrProcessingSettings;
  ocrFormState: OcrSettings;
  hasDirtyPerformanceSettings: boolean;
  hasDirtyOllamaSettings: boolean;
  hasDirtyOfficeSettings: boolean;
  hasDirtyIngestionSettings: boolean;
  hasDirtyOcrProcessingSettings: boolean;
  hasDirtyOcrSettings: boolean;
  setFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setSavedFormState: Dispatch<SetStateAction<OllamaSettings>>;
  setModelToInstall: Dispatch<SetStateAction<string>>;
};

export function createSettingsSectionActions(params: SettingsSectionActionParams) {
  const {
    onDataChanged,
    modelToInstall,
    formState,
    officeFormState,
    performanceFormState,
    ingestionFormState,
    ocrProcessingFormState,
    ocrFormState,
    hasDirtyPerformanceSettings,
    hasDirtyOllamaSettings,
    hasDirtyOfficeSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
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
    setModelToInstall,
    setInfoMessage,
    setErrorMessage,
    setIsBusy
  } = params;

  async function saveSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OllamaSettings>("/api/settings/ollama", {
        method: "PUT",
        body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
      });

      const normalizedSaved = normalizeOllamaSettings(saved);
      setFormState(normalizedSaved);
      setSavedFormState(normalizedSaved);
      setInfoMessage("Impostazioni Ollama salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le impostazioni.");
    } finally {
      setIsBusy(false);
    }
  }

  async function testConnection() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OllamaSettings>("/api/settings/ollama", {
        method: "PUT",
        body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
      });
      const normalizedSaved = normalizeOllamaSettings(saved);
      setFormState(normalizedSaved);
      setSavedFormState(normalizedSaved);

      const response = await apiRequest<OllamaStatusResponse>("/api/ollama/status");
      setInfoMessage(response.message);
      if (!response.isReachable && response.suggestion) {
        setErrorMessage(response.suggestion);
      }

      await refreshDependencyStatus();
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Test connessione non riuscito.");
    } finally {
      setIsBusy(false);
    }
  }

  async function installModel() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>("/api/ollama/models/pull", {
        method: "POST",
        body: JSON.stringify({ name: modelToInstall })
      });

      setInfoMessage(response.message);
      setModelToInstall("");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Installazione modello non riuscita.");
    } finally {
      setIsBusy(false);
    }
  }

  async function removeModel(name: string) {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>(
        `/api/ollama/models?name=${encodeURIComponent(name)}`,
        {
          method: "DELETE"
        }
      );

      setInfoMessage(response.message);
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Rimozione modello non riuscita.");
    } finally {
      setIsBusy(false);
    }
  }

  const dependencyActions = createSettingsSectionDependencyActions(params);
  const { refreshDependencyStatus, refreshOfficeConverter } = dependencyActions;
  const resetActions = createSettingsSectionResetActions({
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
  });

  async function savePerformanceSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<PerformanceSettings>("/api/settings/performance", {
        method: "PUT",
        body: JSON.stringify(buildPerformanceSettingsPayload(performanceFormState))
      });

      const normalizedSaved = normalizePerformanceSettings(saved);
      setPerformanceFormState(normalizedSaved);
      setSavedPerformanceFormState(normalizedSaved);
      setFormState((current: OllamaSettings) => ({
        ...current,
        requestTimeoutSeconds: normalizedSaved.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSaved.embeddingBatchSize
      }));
      setSavedFormState((current: OllamaSettings) => ({
        ...current,
        requestTimeoutSeconds: normalizedSaved.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSaved.embeddingBatchSize
      }));
      setInfoMessage("Impostazioni prestazioni salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le prestazioni.");
    } finally {
      setIsBusy(false);
    }
  }

  async function saveIngestionSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<IngestionSettings>("/api/settings/ingestion", {
        method: "PUT",
        body: JSON.stringify(buildIngestionSettingsPayload(ingestionFormState))
      });

      const normalizedSaved = normalizeIngestionSettings(saved);
      setIngestionFormState(normalizedSaved);
      setSavedIngestionFormState(normalizedSaved);
      setInfoMessage("Impostazioni ingestion salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare ingestion.");
    } finally {
      setIsBusy(false);
    }
  }

  async function saveOcrProcessingSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing", {
        method: "PUT",
        body: JSON.stringify(buildOcrProcessingSettingsPayload(ocrProcessingFormState))
      });

      const normalizedSaved = normalizeOcrProcessingSettings(saved);
      setOcrProcessingFormState(normalizedSaved);
      setSavedOcrProcessingFormState(normalizedSaved);
      setInfoMessage("Impostazioni OCR runtime salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare OCR runtime.");
    } finally {
      setIsBusy(false);
    }
  }

  async function saveOcrSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OcrSettings>("/api/settings/ocr", {
        method: "PUT",
        body: JSON.stringify(buildOcrSettingsPayload(ocrFormState))
      });

      const normalizedSaved = normalizeOcrSettings(saved);
      setOcrFormState(normalizedSaved);
      setSavedOcrFormState(normalizedSaved);
      setInfoMessage("Impostazioni OCR salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le impostazioni OCR.");
    } finally {
      setIsBusy(false);
    }
  }

  function applyOcrProfile(profile: string) {
    setOcrFormState((current: OcrSettings) => {
      const preset = getOcrProfilePreset(profile, current.device);
      return preset ?? { ...current, profile: "custom" };
    });
  }

  function updateOcrSettings(patch: Partial<OcrSettings>) {
    setOcrFormState((current: OcrSettings) => ({ ...current, ...patch, profile: "custom" }));
  }

  async function saveOfficeSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await persistOfficeSettings();
      setOfficeFormState(saved);
      setSavedOfficeFormState(saved);
      setInfoMessage("Impostazioni convertitore Office salvate.");
      await refreshOfficeConverter();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare il convertitore Office.");
    } finally {
      setIsBusy(false);
    }
  }

  async function persistOfficeSettings(): Promise<OfficeConversionSettings> {
    const saved = await apiRequest<OfficeConversionSettings>("/api/settings/office-conversion", {
      method: "PUT",
      body: JSON.stringify(buildOfficeSettingsPayload(officeFormState))
    });
    return normalizeOfficeSettings(saved);
  }

  async function persistAllDirtyChanges() {
    if (hasDirtyPerformanceSettings) {
      const savedPerformance = normalizePerformanceSettings(
        await apiRequest<PerformanceSettings>("/api/settings/performance", {
          method: "PUT",
          body: JSON.stringify(buildPerformanceSettingsPayload(performanceFormState))
        })
      );
      setPerformanceFormState(savedPerformance);
      setSavedPerformanceFormState(savedPerformance);
      setFormState((current: OllamaSettings) => ({
        ...current,
        requestTimeoutSeconds: savedPerformance.requestTimeoutSeconds,
        embeddingBatchSize: savedPerformance.embeddingBatchSize
      }));
      setSavedFormState((current: OllamaSettings) => ({
        ...current,
        requestTimeoutSeconds: savedPerformance.requestTimeoutSeconds,
        embeddingBatchSize: savedPerformance.embeddingBatchSize
      }));
    }

    if (hasDirtyOllamaSettings) {
      const savedSettings = normalizeOllamaSettings(
        await apiRequest<OllamaSettings>("/api/settings/ollama", {
          method: "PUT",
          body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
        })
      );
      setFormState(savedSettings);
      setSavedFormState(savedSettings);
    }

    if (hasDirtyOfficeSettings) {
      const savedOffice = await persistOfficeSettings();
      setOfficeFormState(savedOffice);
      setSavedOfficeFormState(savedOffice);
    }

    if (hasDirtyIngestionSettings) {
      const savedIngestion = normalizeIngestionSettings(
        await apiRequest<IngestionSettings>("/api/settings/ingestion", {
          method: "PUT",
          body: JSON.stringify(buildIngestionSettingsPayload(ingestionFormState))
        })
      );
      setIngestionFormState(savedIngestion);
      setSavedIngestionFormState(savedIngestion);
    }

    if (hasDirtyOcrProcessingSettings) {
      const savedProcessing = normalizeOcrProcessingSettings(
        await apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing", {
          method: "PUT",
          body: JSON.stringify(buildOcrProcessingSettingsPayload(ocrProcessingFormState))
        })
      );
      setOcrProcessingFormState(savedProcessing);
      setSavedOcrProcessingFormState(savedProcessing);
    }

    if (hasDirtyOcrSettings) {
      const savedOcr = normalizeOcrSettings(
        await apiRequest<OcrSettings>("/api/settings/ocr", {
          method: "PUT",
          body: JSON.stringify(buildOcrSettingsPayload(ocrFormState))
        })
      );
      setOcrFormState(savedOcr);
      setSavedOcrFormState(savedOcr);
    }
  }

  return {
    ...dependencyActions,
    saveSettings,
    testConnection,
    installModel,
    removeModel,
    savePerformanceSettings,
    saveIngestionSettings,
    saveOcrProcessingSettings,
    saveOcrSettings,
    applyOcrProfile,
    updateOcrSettings,
    saveOfficeSettings,
    restoreBalancedDefaults: resetActions.restoreBalancedDefaults,
    requestAppDataReset: resetActions.requestAppDataReset,
    persistAllDirtyChanges
  } as const;
}
