import type { Dispatch, SetStateAction } from "react";
import {
  apiRequest,
  type DependencyActionResponse,
  type DiagnosticsResponse,
  type IngestionSettings,
  type OfficeConversionSettings,
  type OfficeConverterStatusResponse,
  type OcrLanguage,
  type OcrProcessingSettings,
  type OcrProvisionStatus,
  type OcrSettings,
  type OllamaInstallStatus,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../api";
import {
  normalizeIngestionSettings,
  normalizeOfficeSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
import { OLLAMA_MODEL_LIBRARY_URL } from "./SettingsSection.defaults";

export type SettingsSectionDependencyActionParams = {
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
  setOcrLanguages: Dispatch<SetStateAction<OcrLanguage[]>>;
  setOfficeStatus: Dispatch<SetStateAction<OfficeConverterStatusResponse | null>>;
  setDiagnostics: Dispatch<SetStateAction<DiagnosticsResponse | null>>;
  setOllamaInstallStatus: Dispatch<SetStateAction<OllamaInstallStatus | null>>;
  setOcrProvisionStatus: Dispatch<SetStateAction<OcrProvisionStatus | null>>;
  setInfoMessage: Dispatch<SetStateAction<string | null>>;
  setErrorMessage: Dispatch<SetStateAction<string | null>>;
  setIsBusy: Dispatch<SetStateAction<boolean>>;
};

export function createSettingsSectionDependencyActions(params: SettingsSectionDependencyActionParams) {
  const {
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
    setOcrLanguages,
    setOfficeStatus,
    setDiagnostics,
    setOllamaInstallStatus,
    setOcrProvisionStatus,
    setInfoMessage,
    setErrorMessage,
    setIsBusy
  } = params;

  function openOllamaModelLibrary() {
    window.open(OLLAMA_MODEL_LIBRARY_URL, "_blank", "noopener,noreferrer");
  }

  async function refreshOfficeConverter() {
    try {
      const [officeSettings, converterStatus] = await Promise.all([
        apiRequest<OfficeConversionSettings>("/api/settings/office-conversion"),
        apiRequest<OfficeConverterStatusResponse>("/api/office-converter/status")
      ]);
      const normalizedOfficeSettings = normalizeOfficeSettings(officeSettings);
      setOfficeFormState(normalizedOfficeSettings);
      setSavedOfficeFormState(normalizedOfficeSettings);
      setOfficeStatus(converterStatus);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere il convertitore Office.");
    }
  }

  async function refreshPerformanceSettings() {
    try {
      const performance = await apiRequest<PerformanceSettings>("/api/settings/performance");
      const normalizedPerformance = normalizePerformanceSettings(performance);
      setPerformanceFormState(normalizedPerformance);
      setSavedPerformanceFormState(normalizedPerformance);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni prestazioni.");
    }
  }

  async function refreshIngestionSettings() {
    try {
      const ingestion = await apiRequest<IngestionSettings>("/api/settings/ingestion");
      const normalizedIngestion = normalizeIngestionSettings(ingestion);
      setIngestionFormState(normalizedIngestion);
      setSavedIngestionFormState(normalizedIngestion);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni ingestion.");
    }
  }

  async function refreshOcrProcessingSettings() {
    try {
      const processing = await apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing");
      const normalizedProcessing = normalizeOcrProcessingSettings(processing);
      setOcrProcessingFormState(normalizedProcessing);
      setSavedOcrProcessingFormState(normalizedProcessing);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni OCR runtime.");
    }
  }

  async function refreshOcrSettings() {
    try {
      const ocr = await apiRequest<OcrSettings>("/api/settings/ocr");
      const normalizedOcr = normalizeOcrSettings(ocr);
      setOcrFormState(normalizedOcr);
      setSavedOcrFormState(normalizedOcr);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni OCR.");
    }
  }

  async function refreshOcrLanguages() {
    try {
      const languages = await apiRequest<OcrLanguage[]>("/api/ocr/languages");
      setOcrLanguages(languages);
    } catch {
      setOcrLanguages([]);
    }
  }

  async function refreshDiagnostics() {
    try {
      const data = await apiRequest<DiagnosticsResponse>("/api/diagnostics");
      setDiagnostics(data);
    } catch {
      // Diagnostics are non-critical; silence the error to avoid overwriting other messages.
    }
  }

  async function refreshDependencyStatus() {
    try {
      const [ollamaDependency, ocrDependency] = await Promise.all([
        apiRequest<OllamaInstallStatus>("/api/dependencies/ollama"),
        apiRequest<OcrProvisionStatus>("/api/dependencies/ocr")
      ]);
      setOllamaInstallStatus(ollamaDependency);
      setOcrProvisionStatus(ocrDependency);
    } catch {
      // Dependency helpers are non-critical; the rest of Settings must remain usable.
    }
  }

  async function installOllama() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<DependencyActionResponse>("/api/dependencies/ollama/install", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
      await refreshDependencyStatus();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Installazione Ollama non avviata.");
    } finally {
      setIsBusy(false);
    }
  }

  async function openLibreOfficeDownload() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<DependencyActionResponse>("/api/dependencies/libreoffice/open-download", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Download LibreOffice non aperto.");
    } finally {
      setIsBusy(false);
    }
  }

  async function configureOcrRuntime() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/provision", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
      await refreshDependencyStatus();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Configurazione OCR non avviata.");
    } finally {
      setIsBusy(false);
    }
  }

  async function openLogsFolder() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>("/api/diagnostics/open-logs-folder", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile aprire la cartella log.");
    } finally {
      setIsBusy(false);
    }
  }

  return {
    openOllamaModelLibrary,
    refreshOfficeConverter,
    refreshPerformanceSettings,
    refreshIngestionSettings,
    refreshOcrProcessingSettings,
    refreshOcrSettings,
    refreshOcrLanguages,
    refreshDiagnostics,
    refreshDependencyStatus,
    installOllama,
    openLibreOfficeDownload,
    configureOcrRuntime,
    openLogsFolder
  } as const;
}
