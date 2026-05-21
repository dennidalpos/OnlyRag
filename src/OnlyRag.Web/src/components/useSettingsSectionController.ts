import { useEffect, useMemo, useState } from "react";
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
  type OllamaModel,
  type OllamaModelDetails,
  type OllamaSettings,
  type OllamaStatusResponse,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import {
  areIngestionSettingsEqual,
  areOfficeSettingsEqual,
  areOcrProcessingSettingsEqual,
  areOcrSettingsEqual,
  areOllamaSettingsEqual,
  arePerformanceSettingsEqual,
  buildContextChunkRecommendation,
  buildEmbeddingRecommendations,
  buildIngestionSettingsPayload,
  buildNumCtxRecommendation,
  buildOcrProcessingSettingsPayload,
  buildOcrSettingsPayload,
  buildOfficeSettingsPayload,
  buildOllamaSettingsPayload,
  buildPerformanceSettingsPayload,
  isNonLocalUrl,
  normalizeIngestionSettings,
  normalizeOfficeSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizeOllamaSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";

export type SettingsSectionProps = {
  settings: OllamaSettings | null;
  status: OllamaStatusResponse | null;
  models: OllamaModel[];
  loadError: string | null;
  onDataChanged: () => Promise<void>;
};

const emptySettings: OllamaSettings = {
  ollamaBaseUrl: "http://localhost:11434",
  defaultChatModel: null,
  defaultEmbeddingModel: null,
  defaultTranslationModel: null,
  requestTimeoutSeconds: 120,
  embeddingBatchSize: 1,
  embeddingNumCtx: null,
  chatNumCtx: null,
  translationNumCtx: null,
  trustNonLocalEndpoint: false
};

const OLLAMA_MODEL_LIBRARY_URL = "https://ollama.com/library";

const emptyOfficeSettings: OfficeConversionSettings = {
  libreOfficePath: null,
  conversionTimeoutSeconds: 120
};

const emptyPerformanceSettings: PerformanceSettings = {
  maxParallelJobs: 1,
  maxOcrParallelPages: 1,
  embeddingBatchSize: 1,
  translationBatchSize: 1,
  maxContextChunks: 8,
  requestTimeoutSeconds: 120,
  enableLowResourceMode: false
};

const emptyIngestionSettings: IngestionSettings = {
  chunkSizeTokens: 800,
  overlapTokens: 120
};

const emptyOcrProcessingSettings: OcrProcessingSettings = {
  language: "it",
  maxRetries: 2,
  pageTimeoutSeconds: 180,
  lowConfidenceThreshold: 0.55
};

const emptyOcrSettings: OcrSettings = {
  profile: "balanced",
  pdfDpi: 200,
  modelPreset: "PP-OCRv5",
  modelVersion: "PP-OCRv5",
  detectionSideLimit: 960,
  detectionThreshold: 0.3,
  detectionBoxThreshold: 0.6,
  detectionUnclipRatio: 1.5,
  recognitionScoreThreshold: 0.5,
  useTextlineOrientation: true,
  useDocumentOrientationClassification: false,
  useDocumentUnwarping: false,
  recognitionBatchSize: 6,
  cpuThreads: 2,
  device: "cpu"
};

const ocrProfilePresets: Record<string, OcrSettings> = {
  fast: {
    ...emptyOcrSettings,
    profile: "fast",
    pdfDpi: 150,
    detectionSideLimit: 736,
    detectionThreshold: 0.35,
    detectionBoxThreshold: 0.65,
    detectionUnclipRatio: 1.4,
    recognitionScoreThreshold: 0.55,
    recognitionBatchSize: 4,
    cpuThreads: 1
  },
  balanced: emptyOcrSettings,
  accurate: {
    ...emptyOcrSettings,
    profile: "accurate",
    pdfDpi: 300,
    detectionSideLimit: 1280,
    detectionThreshold: 0.25,
    detectionBoxThreshold: 0.55,
    detectionUnclipRatio: 1.7,
    recognitionScoreThreshold: 0.45,
    useDocumentOrientationClassification: true,
    useDocumentUnwarping: true,
    recognitionBatchSize: 8,
    cpuThreads: 4
  }
};



export function useSettingsSectionController({
  settings,
  status,
  models,
  loadError,
  onDataChanged
}: SettingsSectionProps) {
  const [formState, setFormState] = useState<OllamaSettings>(emptySettings);
  const [savedFormState, setSavedFormState] = useState<OllamaSettings>(emptySettings);
  const [officeFormState, setOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
  const [savedOfficeFormState, setSavedOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
  const [performanceFormState, setPerformanceFormState] = useState<PerformanceSettings>(emptyPerformanceSettings);
  const [savedPerformanceFormState, setSavedPerformanceFormState] = useState<PerformanceSettings>(emptyPerformanceSettings);
  const [ingestionFormState, setIngestionFormState] = useState<IngestionSettings>(emptyIngestionSettings);
  const [savedIngestionFormState, setSavedIngestionFormState] = useState<IngestionSettings>(emptyIngestionSettings);
  const [ocrProcessingFormState, setOcrProcessingFormState] =
    useState<OcrProcessingSettings>(emptyOcrProcessingSettings);
  const [savedOcrProcessingFormState, setSavedOcrProcessingFormState] =
    useState<OcrProcessingSettings>(emptyOcrProcessingSettings);
  const [ocrFormState, setOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [savedOcrFormState, setSavedOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [ocrLanguages, setOcrLanguages] = useState<OcrLanguage[]>([]);
  const [officeStatus, setOfficeStatus] = useState<OfficeConverterStatusResponse | null>(null);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsResponse | null>(null);
  const [ollamaInstallStatus, setOllamaInstallStatus] = useState<OllamaInstallStatus | null>(null);
  const [ocrProvisionStatus, setOcrProvisionStatus] = useState<OcrProvisionStatus | null>(null);
  const [modelToInstall, setModelToInstall] = useState("");
  const [infoMessage, setInfoMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [embeddingModelDetails, setEmbeddingModelDetails] = useState<OllamaModelDetails | null>(null);
  const [chatModelDetails, setChatModelDetails] = useState<OllamaModelDetails | null>(null);
  const [translationModelDetails, setTranslationModelDetails] = useState<OllamaModelDetails | null>(null);
  const [embeddingModelDetailsLoading, setEmbeddingModelDetailsLoading] = useState(false);
  const [chatModelDetailsLoading, setChatModelDetailsLoading] = useState(false);
  const [translationModelDetailsLoading, setTranslationModelDetailsLoading] = useState(false);

  useEffect(() => {
    if (settings) {
      const normalizedSettings = normalizeOllamaSettings(settings);
      setFormState(normalizedSettings);
      setSavedFormState(normalizedSettings);
      setPerformanceFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSettings.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSettings.embeddingBatchSize
      }));
      setSavedPerformanceFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSettings.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSettings.embeddingBatchSize
      }));
    }
  }, [settings]);

  useEffect(() => {
    void refreshOfficeConverter();
    void refreshPerformanceSettings();
    void refreshIngestionSettings();
    void refreshOcrProcessingSettings();
    void refreshOcrSettings();
    void refreshOcrLanguages();
    void refreshDiagnostics();
    void refreshDependencyStatus();
  }, []);

  useEffect(() => {
    const modelName = formState.defaultEmbeddingModel;
    if (!modelName) {
      setEmbeddingModelDetails(null);
      return;
    }

    let cancelled = false;
    setEmbeddingModelDetailsLoading(true);
    apiRequest<OllamaModelDetails>(`/api/ollama/models/${encodeURIComponent(modelName)}/details`)
      .then((details) => { if (!cancelled) { setEmbeddingModelDetails(details); } })
      .catch(() => { if (!cancelled) { setEmbeddingModelDetails(null); } })
      .finally(() => { if (!cancelled) { setEmbeddingModelDetailsLoading(false); } });

    return () => { cancelled = true; };
  }, [formState.defaultEmbeddingModel]);

  useEffect(() => {
    const modelName = formState.defaultChatModel;
    if (!modelName) {
      setChatModelDetails(null);
      return;
    }

    let cancelled = false;
    setChatModelDetailsLoading(true);
    apiRequest<OllamaModelDetails>(`/api/ollama/models/${encodeURIComponent(modelName)}/details`)
      .then((details) => { if (!cancelled) { setChatModelDetails(details); } })
      .catch(() => { if (!cancelled) { setChatModelDetails(null); } })
      .finally(() => { if (!cancelled) { setChatModelDetailsLoading(false); } });

    return () => { cancelled = true; };
  }, [formState.defaultChatModel]);

  useEffect(() => {
    const modelName = formState.defaultTranslationModel;
    if (!modelName) {
      setTranslationModelDetails(null);
      return;
    }

    let cancelled = false;
    setTranslationModelDetailsLoading(true);
    apiRequest<OllamaModelDetails>(`/api/ollama/models/${encodeURIComponent(modelName)}/details`)
      .then((details) => { if (!cancelled) { setTranslationModelDetails(details); } })
      .catch(() => { if (!cancelled) { setTranslationModelDetails(null); } })
      .finally(() => { if (!cancelled) { setTranslationModelDetailsLoading(false); } });

    return () => { cancelled = true; };
  }, [formState.defaultTranslationModel]);

  const installedModelNames = useMemo(() => models.map((model) => model.name), [models]);
  const usesNonLocalOllamaEndpoint = useMemo(
    () => isNonLocalUrl(formState.ollamaBaseUrl),
    [formState.ollamaBaseUrl]
  );
  const unavailableDefaults = useMemo(
    () =>
      [
        formState.defaultChatModel,
        formState.defaultEmbeddingModel,
        formState.defaultTranslationModel
      ].filter((value): value is string => Boolean(value && !installedModelNames.includes(value))),
    [
      formState.defaultChatModel,
      formState.defaultEmbeddingModel,
      formState.defaultTranslationModel,
      installedModelNames
    ]
  );
  const embeddingRecommendations = useMemo(
    () => buildEmbeddingRecommendations(embeddingModelDetails?.numCtx ?? null),
    [embeddingModelDetails]
  );
  const chatNumCtxRecommendation = useMemo(
    () => buildNumCtxRecommendation(chatModelDetails?.numCtx ?? null),
    [chatModelDetails]
  );
  const translationNumCtxRecommendation = useMemo(
    () => buildNumCtxRecommendation(translationModelDetails?.numCtx ?? null),
    [translationModelDetails]
  );
  const recommendedMaxContextChunks = useMemo(
    () => buildContextChunkRecommendation(chatModelDetails?.numCtx ?? embeddingModelDetails?.numCtx ?? null),
    [chatModelDetails, embeddingModelDetails]
  );
  const hasDirtyOllamaSettings = !areOllamaSettingsEqual(formState, savedFormState);
  const hasDirtyOfficeSettings = !areOfficeSettingsEqual(officeFormState, savedOfficeFormState);
  const hasDirtyPerformanceSettings = !arePerformanceSettingsEqual(
    performanceFormState,
    savedPerformanceFormState
  );
  const hasDirtyIngestionSettings = !areIngestionSettingsEqual(ingestionFormState, savedIngestionFormState);
  const hasDirtyOcrProcessingSettings = !areOcrProcessingSettingsEqual(
    ocrProcessingFormState,
    savedOcrProcessingFormState
  );
  const hasDirtyOcrSettings = !areOcrSettingsEqual(ocrFormState, savedOcrFormState);
  const hasPendingChanges =
    hasDirtyOllamaSettings
    || hasDirtyOfficeSettings
    || hasDirtyPerformanceSettings
    || hasDirtyIngestionSettings
    || hasDirtyOcrProcessingSettings
    || hasDirtyOcrSettings;

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
        `/api/ollama/models/${encodeURIComponent(name)}`,
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
      setFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSaved.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSaved.embeddingBatchSize
      }));
      setSavedFormState((current) => ({
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
    const preset = ocrProfilePresets[profile];
    setOcrFormState((current) => (preset ? { ...preset } : { ...current, profile: "custom" }));
  }

  function updateOcrSettings(patch: Partial<OcrSettings>) {
    setOcrFormState((current) => ({ ...current, ...patch, profile: "custom" }));
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
      setFormState((current) => ({
        ...current,
        requestTimeoutSeconds: savedPerformance.requestTimeoutSeconds,
        embeddingBatchSize: savedPerformance.embeddingBatchSize
      }));
      setSavedFormState((current) => ({
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

  useEffect(() => {
    setExitContributor("settings", {
      label: "Impostazioni",
      hasPendingChanges,
      hasActiveWork: isBusy,
      prepareForExit: persistAllDirtyChanges
    });

    return () => {
      clearExitContributor("settings");
    };
  }, [
    formState,
    hasDirtyIngestionSettings,
    hasDirtyOfficeSettings,
    hasDirtyOllamaSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasDirtyPerformanceSettings,
    hasPendingChanges,
    isBusy,
    ingestionFormState,
    ocrFormState,
    ocrProcessingFormState,
    officeFormState,
    performanceFormState
  ]);

  useEffect(() => {
    if (!ocrProvisionStatus?.isRunning) {
      return;
    }

    const interval = window.setInterval(() => {
      void refreshDependencyStatus();
      void refreshDiagnostics();
    }, 5000);

    return () => window.clearInterval(interval);
  }, [ocrProvisionStatus?.isRunning]);

  return {
    settings,
    status,
    models,
    loadError,
    formState,
    setFormState,
    officeFormState,
    setOfficeFormState,
    performanceFormState,
    setPerformanceFormState,
    ingestionFormState,
    setIngestionFormState,
    ocrProcessingFormState,
    setOcrProcessingFormState,
    ocrFormState,
    modelToInstall,
    setModelToInstall,
    officeStatus,
    diagnostics,
    ollamaInstallStatus,
    ocrProvisionStatus,
    ocrLanguages,
    embeddingModelDetails,
    chatModelDetails,
    translationModelDetails,
    embeddingModelDetailsLoading,
    chatModelDetailsLoading,
    translationModelDetailsLoading,
    usesNonLocalOllamaEndpoint,
    unavailableDefaults,
    embeddingRecommendations,
    chatNumCtxRecommendation,
    translationNumCtxRecommendation,
    recommendedMaxContextChunks,
    hasDirtyOllamaSettings,
    hasDirtyOfficeSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges,
    infoMessage,
    errorMessage,
    isBusy,
    saveSettings,
    testConnection,
    installModel,
    removeModel,
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
    openLogsFolder,
    saveOfficeSettings,
    savePerformanceSettings,
    saveIngestionSettings,
    saveOcrProcessingSettings,
    applyOcrProfile,
    updateOcrSettings,
    saveOcrSettings
  } as const;
}

export type SettingsSectionController = ReturnType<typeof useSettingsSectionController>;

