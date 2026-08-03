import type { Dispatch, SetStateAction } from "react";
import {
  apiRequest,
  type DependencyActionResponse,
  type DiagnosticsResponse,
  type IngestionSettings,
  type PdfExportSettings,
  type PdfExportConverterStatusResponse,
  type OcrLanguage,
  type OcrAutoGpuEnableResponse,
  type OcrProcessingSettings,
  type OcrProvisionRequest,
  type OcrProvisionStatus,
  type OcrSettings,
  type OllamaInstallStatus,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../../api";
import {
  normalizeIngestionSettings,
  normalizePdfExportSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
import { OLLAMA_MODEL_LIBRARY_URL } from "./SettingsSection.defaults";

export type SettingsSectionDependencyActionParams = {
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
  setOcrLanguages: Dispatch<SetStateAction<OcrLanguage[]>>;
  setPdfExportStatus: Dispatch<SetStateAction<PdfExportConverterStatusResponse | null>>;
  setDiagnostics: Dispatch<SetStateAction<DiagnosticsResponse | null>>;
  setDiagnosticsStatus: Dispatch<SetStateAction<"loading" | "ready" | "unavailable">>;
  setOllamaInstallStatus: Dispatch<SetStateAction<OllamaInstallStatus | null>>;
  setOcrProvisionStatus: Dispatch<SetStateAction<OcrProvisionStatus | null>>;
  setInfoMessage: Dispatch<SetStateAction<string | null>>;
  setErrorMessage: Dispatch<SetStateAction<string | null>>;
  setIsBusy: Dispatch<SetStateAction<boolean>>;
};

export function createSettingsSectionDependencyActions(params: SettingsSectionDependencyActionParams) {
  const {
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
    setOcrLanguages,
    setPdfExportStatus,
    setDiagnostics,
    setDiagnosticsStatus,
    setOllamaInstallStatus,
    setOcrProvisionStatus,
    setInfoMessage,
    setErrorMessage,
    setIsBusy
  } = params;

  function openOllamaModelLibrary() {
    window.open(OLLAMA_MODEL_LIBRARY_URL, "_blank", "noopener,noreferrer");
  }

  async function refreshPdfExportConverter() {
    try {
      const [pdfExportSettings, converterStatus] = await Promise.all([
        apiRequest<PdfExportSettings>("/api/settings/pdf-export"),
        apiRequest<PdfExportConverterStatusResponse>("/api/pdf-export/status")
      ]);
      const normalizedPdfExportSettings = normalizePdfExportSettings(pdfExportSettings);
      setPdfExportFormState(normalizedPdfExportSettings);
      setSavedPdfExportFormState(normalizedPdfExportSettings);
      setPdfExportStatus(converterStatus);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni export PDF.");
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
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni di ingestione.");
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
      return normalizedOcr;
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni OCR.");
      return null;
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
    setDiagnosticsStatus((current) => current === "ready" ? current : "loading");
    try {
      const data = await apiRequest<DiagnosticsResponse>("/api/diagnostics");
      setDiagnostics(data);
      setDiagnosticsStatus("ready");
      return data;
    } catch {
      setDiagnostics(null);
      setDiagnosticsStatus("unavailable");
      // Diagnostics are non-critical; silence the error to avoid overwriting other messages.
      return null;
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
      if (ocrDependency.isConfigured && !ocrDependency.isRunning) {
        await refreshConfiguredOcrRuntime();
      }
    } catch {
      // Dependency helpers are non-critical; the rest of Settings must remain usable.
    }
  }

  async function refreshConfiguredOcrRuntime() {
    const diagnostics = await refreshDiagnostics();
    if (diagnostics?.ocrGpuCapability.isUsable) {
      const autoGpu = await apiRequest<OcrAutoGpuEnableResponse>("/api/settings/ocr/auto-enable-gpu", {
        method: "POST"
      }).catch(() => null);

      if (autoGpu) {
        const normalizedOcr = normalizeOcrSettings(autoGpu.settings);
        setOcrFormState(normalizedOcr);
        setSavedOcrFormState(normalizedOcr);
      }
    }

    await refreshOcrSettings();
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

  async function configureOcrRuntime(runtimeTarget: OcrProvisionRequest["runtimeTarget"] = "auto") {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const request: OcrProvisionRequest = { confirmed: true, runtimeTarget };
      const response = await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/provision", {
        method: "POST",
        body: JSON.stringify(request)
      });
      setInfoMessage(response.message);
      await refreshDependencyStatus();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Configurazione OCR non avviata.");
    } finally {
      setIsBusy(false);
    }
  }

  async function cancelOcrRuntimeConfiguration() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<DependencyActionResponse>("/api/dependencies/ocr/cancel", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setInfoMessage(response.message);
      await refreshDependencyStatus();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Annullamento OCR non avviato.");
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
    refreshPdfExportConverter,
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
    cancelOcrRuntimeConfiguration,
    openLogsFolder
  } as const;
}
