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
  AdjustableModelContextBar,
  OcrFieldLabel,
  OcrRangeField,
  SettingsRangeField,
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
  formatModelSize,
  formatOcrDecimal,
  getOcrLanguageOptions,
  getOcrSelectOptions,
  isNonLocalUrl,
  normalizeIngestionSettings,
  normalizeOptionalValue,
  normalizeOfficeSettings,
  normalizeOcrProcessingSettings,
  normalizeOcrSettings,
  normalizeOllamaSettings,
  normalizePerformanceSettings
} from "./SettingsSection.helpers";
type SettingsSectionProps = {
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

const PADDLE_OCR_MODEL_PRESETS = ["PP-OCRv5"];
const PADDLE_OCR_MODEL_VERSIONS = ["PP-OCRv5"];

export function SettingsSection({
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

  return (
    <div className="section-layout settings-layout">
      <div className="section-copy settings-copy">
        <h2>Impostazioni</h2>
      </div>
      <div className="settings-panel settings-panel--grid" aria-label="Impostazioni principali">
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Connessione Ollama</h3>
            <span className={`status-chip status-chip--${status?.isReachable ? "online" : "offline"}`}>
              {status?.isReachable ? "Online" : "Offline"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="ollama-url">
              <span>URL Ollama</span>
              <input
                id="ollama-url"
                type="url"
                value={formState.ollamaBaseUrl}
                onChange={(event) =>
                  setFormState((current) => ({ ...current, ollamaBaseUrl: event.target.value }))
                }
                placeholder="http://localhost:11434"
              />
            </label>
            {usesNonLocalOllamaEndpoint && (
              <label className="toggle-row" htmlFor="trust-non-local-ollama">
                <input
                  id="trust-non-local-ollama"
                  type="checkbox"
                  checked={formState.trustNonLocalEndpoint}
                  onChange={(event) =>
                    setFormState((current) => ({
                      ...current,
                      trustNonLocalEndpoint: event.target.checked
                    }))
                  }
                />
                <span>Considera attendibile questo endpoint Ollama non locale</span>
              </label>
            )}
            <div className="settings-actions">
              <button type="button" onClick={saveSettings} disabled={isBusy}>
                Salva impostazioni
              </button>
              <button type="button" className="button-secondary" onClick={testConnection} disabled={isBusy}>
                Test connessione
              </button>
              {ollamaInstallStatus && !ollamaInstallStatus.cliInstalled && (
                <button type="button" className="button-secondary" onClick={installOllama} disabled={isBusy}>
                  Apri download Ollama
                </button>
              )}
            </div>
            <div className="panel-note">
              <p>{status?.message ?? loadError ?? "Configura l'indirizzo Ollama e testa la connessione."}</p>
              {status?.suggestion && <p>{status.suggestion}</p>}
              {ollamaInstallStatus && !ollamaInstallStatus.cliInstalled && (
                <p>Ollama non risulta installato. Il pulsante apre la pagina ufficiale: <code>{ollamaInstallStatus.installCommand}</code></p>
              )}
              {ollamaInstallStatus && (
                <p>{ollamaInstallStatus.networkAccessHint}</p>
              )}
              {usesNonLocalOllamaEndpoint && (
                <p>Chat, embedding e traduzione inviano testo all'endpoint configurato. Abilita la fiducia solo per un servizio Ollama che controlli su una rete attendibile.</p>
              )}
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Prestazioni</h3>
            {performanceFormState.enableLowResourceMode && (
              <span className="status-chip status-chip--offline">Modalità risparmio risorse</span>
            )}
          </div>
          <div className="settings-form">
            <label className="toggle-row" htmlFor="low-resource-mode">
              <input
                id="low-resource-mode"
                type="checkbox"
                checked={performanceFormState.enableLowResourceMode}
                onChange={(event) =>
                  setPerformanceFormState((current) => ({
                    ...current,
                    enableLowResourceMode: event.target.checked
                  }))
                }
              />
              <span>Modalità PC poco performante</span>
            </label>
            {performanceFormState.enableLowResourceMode && (
              <div className="panel-note" style={{ marginTop: 0 }}>
                <p>Forza job paralleli, batch OCR, embedding e traduzione a 1. Consigliato su macchine con meno di 8 GB di RAM o CPU lenta.</p>
              </div>
            )}
            <div className="settings-grid">
              <SettingsRangeField
                id="max-parallel-jobs"
                label="Job paralleli"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxParallelJobs}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxParallelJobs: value }))
                }
              />
              <SettingsRangeField
                id="ocr-parallel-pages"
                label="Pagine OCR parallele"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxOcrParallelPages}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxOcrParallelPages: value }))
                }
              />
              <SettingsRangeField
                id="performance-embedding-batch"
                label="Batch embedding"
                min={1}
                max={8}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.embeddingBatchSize}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, embeddingBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="translation-batch-size"
                label="Batch traduzione"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.translationBatchSize}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, translationBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="max-context-chunks"
                label="Chunk contesto"
                min={1}
                max={24}
                value={performanceFormState.maxContextChunks}
                hint={recommendedMaxContextChunks ? `Suggerito: ${recommendedMaxContextChunks}` : null}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxContextChunks: value }))
                }
              />
              <SettingsRangeField
                id="performance-request-timeout"
                label="Timeout richieste"
                min={5}
                max={600}
                value={performanceFormState.requestTimeoutSeconds}
                formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, requestTimeoutSeconds: value }))
                }
              />
            </div>
            {(chatModelDetailsLoading || embeddingModelDetailsLoading) && (
              <div className="panel-note">
                <p>Lettura dettagli modello in corso.</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="button" onClick={savePerformanceSettings} disabled={isBusy}>
                Salva prestazioni
              </button>
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>OCR PaddleOCR</h3>
            <span className="status-chip status-chip--muted">{ocrFormState.profile}</span>
          </div>
          <div className="settings-form">
            <div className="settings-grid">
              <label className="field-group" htmlFor="ocr-profile">
                <OcrFieldLabel
                  text="Profilo"
                  tooltip="Profilo generale del bridge OCR. Veloce riduce costo, accurato privilegia qualita e controlli piu conservativi."
                />
                <select
                  id="ocr-profile"
                  value={ocrFormState.profile}
                  onChange={(event) => applyOcrProfile(event.target.value)}
                >
                  <option value="fast">Veloce</option>
                  <option value="balanced">Bilanciato</option>
                  <option value="accurate">Accurato</option>
                  <option value="custom">Personalizzato</option>
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-device">
                <OcrFieldLabel
                  text="Dispositivo"
                  tooltip="CPU e' piu compatibile. GPU richiede un ambiente PaddleOCR configurato per accelerazione hardware."
                />
                <select
                  id="ocr-device"
                  value={ocrFormState.device}
                  onChange={(event) => updateOcrSettings({ device: event.target.value })}
                >
                  <option value="cpu">CPU</option>
                  <option value="gpu">GPU</option>
                </select>
              </label>
              <OcrRangeField
                id="ocr-pdf-dpi"
                label="DPI PDF"
                tooltip="Risoluzione usata per convertire pagine PDF in immagini prima dell'OCR. Valori bassi sono piu veloci, valori alti leggono meglio testi piccoli."
                min={96}
                max={400}
                value={ocrFormState.pdfDpi}
                onChange={(value) => updateOcrSettings({ pdfDpi: value })}
              />
              <label className="field-group" htmlFor="ocr-model-preset">
                <OcrFieldLabel
                  text="Preset modello"
                  tooltip="Preset PaddleOCR passato al bridge. Il menu mostra i preset noti nel progetto e conserva eventuali valori gia salvati."
                />
                <select
                  id="ocr-model-preset"
                  value={ocrFormState.modelPreset}
                  onChange={(event) => updateOcrSettings({ modelPreset: event.target.value })}
                >
                  {getOcrSelectOptions(ocrFormState.modelPreset, PADDLE_OCR_MODEL_PRESETS).map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-model-version">
                <OcrFieldLabel
                  text="Versione modello"
                  tooltip="Versione OCR passata a PaddleOCR come ocr_version quando supportata. Il valore salvato resta selezionabile anche se non e' nell'elenco noto."
                />
                <select
                  id="ocr-model-version"
                  value={ocrFormState.modelVersion}
                  onChange={(event) => updateOcrSettings({ modelVersion: event.target.value })}
                >
                  {getOcrSelectOptions(ocrFormState.modelVersion, PADDLE_OCR_MODEL_VERSIONS).map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <OcrRangeField
                id="ocr-detection-side-limit"
                label="Lato massimo detection"
                tooltip="Dimensione massima usata dal detector testo. Valori bassi riducono tempo e memoria, valori alti aiutano pagine grandi o dettagli fini."
                min={320}
                max={4096}
                value={ocrFormState.detectionSideLimit}
                onChange={(value) => updateOcrSettings({ detectionSideLimit: value })}
              />
              <OcrRangeField
                id="ocr-detection-threshold"
                label="Soglia detection"
                tooltip="Confidenza minima per proporre aree di testo. Valori bassi rilevano piu elementi, valori alti scartano rumore."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.detectionThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionThreshold: value })}
              />
              <OcrRangeField
                id="ocr-detection-box-threshold"
                label="Soglia box"
                tooltip="Filtro sui riquadri rilevati. Valori bassi sono piu permissivi, valori alti tengono solo box piu affidabili."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.detectionBoxThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionBoxThreshold: value })}
              />
              <OcrRangeField
                id="ocr-detection-unclip-ratio"
                label="Unclip ratio"
                tooltip="Espansione dei box di testo rilevati. Valori bassi sono piu stretti, valori alti includono piu margine intorno al testo."
                min={1}
                max={3}
                step={0.05}
                value={ocrFormState.detectionUnclipRatio}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionUnclipRatio: value })}
              />
              <OcrRangeField
                id="ocr-recognition-score-threshold"
                label="Soglia riconoscimento"
                tooltip="Confidenza minima delle parole riconosciute. Valori bassi mantengono piu testo, valori alti privilegiano risultati piu affidabili."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.recognitionScoreThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ recognitionScoreThreshold: value })}
              />
              <OcrRangeField
                id="ocr-recognition-batch-size"
                label="Batch riconoscimento"
                tooltip="Numero di crop di testo riconosciuti insieme. Valori bassi consumano meno memoria, valori alti possono accelerare su hardware adeguato."
                min={1}
                max={32}
                value={ocrFormState.recognitionBatchSize}
                onChange={(value) => updateOcrSettings({ recognitionBatchSize: value })}
              />
              <OcrRangeField
                id="ocr-cpu-threads"
                label="Thread CPU"
                tooltip="Thread CPU dedicati a PaddleOCR. Valori bassi lasciano il PC piu reattivo, valori alti possono ridurre i tempi OCR."
                min={1}
                max={16}
                value={ocrFormState.cpuThreads}
                onChange={(value) => updateOcrSettings({ cpuThreads: value })}
              />
            </div>
            <label className="toggle-row" htmlFor="ocr-textline-orientation">
              <input
                id="ocr-textline-orientation"
                type="checkbox"
                checked={ocrFormState.useTextlineOrientation}
                onChange={(event) =>
                  updateOcrSettings({ useTextlineOrientation: event.target.checked })
                }
              />
              <span>Orientamento righe testo</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-orientation">
              <input
                id="ocr-document-orientation"
                type="checkbox"
                checked={ocrFormState.useDocumentOrientationClassification}
                onChange={(event) =>
                  updateOcrSettings({ useDocumentOrientationClassification: event.target.checked })
                }
              />
              <span>Classificazione orientamento documento</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-unwarping">
              <input
                id="ocr-document-unwarping"
                type="checkbox"
                checked={ocrFormState.useDocumentUnwarping}
                onChange={(event) =>
                  updateOcrSettings({ useDocumentUnwarping: event.target.checked })
                }
              />
              <span>Correzione deformazione documento</span>
            </label>
            <div className="settings-actions">
              <button type="button" onClick={saveOcrSettings} disabled={isBusy}>
                Salva OCR
              </button>
              {hasDirtyOcrSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Ingestion</h3>
            {embeddingRecommendations && (
              <span className="status-chip status-chip--muted">
                {embeddingRecommendations.chunkMinimum.toLocaleString("it-IT")}-
                {embeddingRecommendations.chunkMaximum.toLocaleString("it-IT")}
              </span>
            )}
          </div>
          <div className="settings-form">
            <SettingsRangeField
              id="ingestion-chunk-size"
              label="Dimensione chunk"
              min={100}
              max={4000}
              step={50}
              value={ingestionFormState.chunkSizeTokens}
              formatValue={(value) => `${value.toLocaleString("it-IT")} token`}
              hint={embeddingRecommendations ? `Suggerito: ${embeddingRecommendations.chunkMinimum}-${embeddingRecommendations.chunkMaximum}` : null}
              onChange={(value) =>
                setIngestionFormState((current) => {
                  const nextChunkSize = value;
                  return {
                    chunkSizeTokens: nextChunkSize,
                    overlapTokens: Math.min(current.overlapTokens, Math.min(1000, Math.floor(nextChunkSize / 2)))
                  };
                })
              }
            />
            <SettingsRangeField
              id="ingestion-overlap"
              label="Overlap chunk"
              min={0}
              max={Math.min(1000, Math.floor(ingestionFormState.chunkSizeTokens / 2))}
              step={10}
              value={ingestionFormState.overlapTokens}
              formatValue={(value) => `${value.toLocaleString("it-IT")} token`}
              onChange={(value) =>
                setIngestionFormState((current) => ({ ...current, overlapTokens: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveIngestionSettings} disabled={isBusy}>
                Salva ingestion
              </button>
              {hasDirtyIngestionSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>OCR runtime</h3>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="ocr-processing-language">
              <span>Lingua OCR</span>
              <select
                id="ocr-processing-language"
                value={ocrProcessingFormState.language}
                onChange={(event) =>
                  setOcrProcessingFormState((current) => ({ ...current, language: event.target.value }))
                }
              >
                {getOcrLanguageOptions(ocrProcessingFormState.language, ocrLanguages).map((language) => (
                  <option key={language.code} value={language.code}>
                    {language.label}
                  </option>
                ))}
              </select>
            </label>
            <SettingsRangeField
              id="ocr-processing-retries"
              label="Retry OCR"
              min={0}
              max={2}
              value={ocrProcessingFormState.maxRetries}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, maxRetries: value }))
              }
            />
            <SettingsRangeField
              id="ocr-processing-timeout"
              label="Timeout pagina"
              min={15}
              max={600}
              step={15}
              value={ocrProcessingFormState.pageTimeoutSeconds}
              formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, pageTimeoutSeconds: value }))
              }
            />
            <SettingsRangeField
              id="ocr-processing-low-confidence"
              label="Soglia bassa confidenza"
              min={0.01}
              max={0.99}
              step={0.01}
              value={ocrProcessingFormState.lowConfidenceThreshold}
              formatValue={(value) => value.toFixed(2)}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, lowConfidenceThreshold: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveOcrProcessingSettings} disabled={isBusy}>
                Salva OCR runtime
              </button>
              {hasDirtyOcrProcessingSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Modelli predefiniti</h3>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="default-chat-model">
              <span>Chat</span>
              <select
                id="default-chat-model"
                value={formState.defaultChatModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultChatModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {formState.defaultChatModel && (
              <AdjustableModelContextBar
                title="Finestra di contesto chat (num_ctx)"
                sliderLabel="num_ctx chat"
                loading={chatModelDetailsLoading}
                details={chatModelDetails}
                fallbackText="Dettagli chat non disponibili."
                value={formState.chatNumCtx}
                recommendedValue={chatNumCtxRecommendation}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    chatNumCtx: isAutomatic ? null : chatNumCtxRecommendation ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, chatNumCtx: value }))
                }
              />
            )}
            <label className="field-group" htmlFor="default-embedding-model">
              <span>Embeddings</span>
              <select
                id="default-embedding-model"
                value={formState.defaultEmbeddingModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultEmbeddingModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>

            {formState.defaultEmbeddingModel && (
              <AdjustableModelContextBar
                title="Finestra di contesto embedding (num_ctx)"
                sliderLabel="num_ctx embedding"
                loading={embeddingModelDetailsLoading}
                details={embeddingModelDetails}
                fallbackText="Dettagli embedding non disponibili."
                value={formState.embeddingNumCtx}
                recommendedValue={embeddingRecommendations?.embeddingNumCtx ?? null}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    embeddingNumCtx: isAutomatic ? null : embeddingRecommendations?.embeddingNumCtx ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, embeddingNumCtx: value }))
                }
              />
            )}
            <label className="field-group" htmlFor="default-translation-model">
              <span>Traduzione</span>
              <select
                id="default-translation-model"
                value={formState.defaultTranslationModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultTranslationModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {formState.defaultTranslationModel && (
              <AdjustableModelContextBar
                title="Finestra di contesto traduzione (num_ctx)"
                sliderLabel="num_ctx traduzione"
                loading={translationModelDetailsLoading}
                details={translationModelDetails}
                fallbackText="Dettagli traduzione non disponibili."
                value={formState.translationNumCtx}
                recommendedValue={translationNumCtxRecommendation}
                onAutoChange={(isAutomatic) =>
                  setFormState((current) => ({
                    ...current,
                    translationNumCtx: isAutomatic ? null : translationNumCtxRecommendation ?? 2048
                  }))
                }
                onValueChange={(value) =>
                  setFormState((current) => ({ ...current, translationNumCtx: value }))
                }
              />
            )}
            {unavailableDefaults.length > 0 && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>Alcuni modelli salvati non sono piu presenti in Ollama: {unavailableDefaults.join(", ")}.</p>
              </div>
            )}
            {hasDirtyOllamaSettings && (
              <div className="settings-actions settings-actions--dirty" aria-live="polite">
                <button type="button" onClick={saveSettings} disabled={isBusy}>
                  Salva modelli predefiniti
                </button>
                <span className="dirty-hint">Modifiche non salvate</span>
              </div>
            )}
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Supporto file Office vecchi (.doc, .xls, .ppt)</h3>
            <span className={`status-chip status-chip--${officeStatus?.isAvailable ? "online" : "offline"}`}>
              {officeStatus?.isAvailable ? "Disponibile" : "Non installato"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="libreoffice-path">
              <span>Percorso LibreOffice (opzionale — rilevato automaticamente se installato)</span>
              <input
                id="libreoffice-path"
                type="text"
                value={officeFormState.libreOfficePath ?? ""}
                onChange={(event) =>
                  setOfficeFormState((current) => ({
                    ...current,
                    libreOfficePath: normalizeOptionalValue(event.target.value)
                  }))
                }
                placeholder="C:\Program Files\LibreOffice\program\soffice.exe"
              />
            </label>
            <SettingsRangeField
              id="office-conversion-timeout"
              label="Timeout conversione"
              min={10}
              max={900}
              step={10}
              value={officeFormState.conversionTimeoutSeconds}
              formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
              onChange={(value) =>
                setOfficeFormState((current) => ({ ...current, conversionTimeoutSeconds: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveOfficeSettings} disabled={isBusy}>
                Salva
              </button>
              {officeStatus && !officeStatus.isAvailable && (
                <button type="button" className="button-secondary" onClick={openLibreOfficeDownload} disabled={isBusy}>
                  Scarica LibreOffice
                </button>
              )}
            </div>
            {officeStatus?.executablePath && (
              <div className="panel-note">
                <p>Rilevato: {officeStatus.executablePath}</p>
              </div>
            )}
            {officeStatus?.suggestion && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>{officeStatus.suggestion}</p>
              </div>
            )}
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Gestione modelli</h3>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="model-install">
              <span>Modello da installare</span>
              <input
                id="model-install"
                type="text"
                value={modelToInstall}
                onChange={(event) => setModelToInstall(event.target.value)}
                placeholder="es. gemma3:4b"
              />
            </label>
            <div className="settings-actions">
              <button
                type="button"
                onClick={installModel}
                disabled={isBusy || modelToInstall.trim().length === 0}
              >
                Installa
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={openOllamaModelLibrary}
              >
                Elenco modelli Ollama
              </button>
            </div>
            <div className="model-list" aria-label="Modelli installati">
              {models.length === 0 && (
                <div className="model-row model-row--empty">
                  <p>Nessun modello installato.</p>
                </div>
              )}
              {models.map((model) => (
                <div className="model-row" key={model.name}>
                  <div>
                    <strong>{model.name}</strong>
                    <span>
                      {model.family ?? "Famiglia non indicata"} | {formatModelSize(model.size)}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => void removeModel(model.name)}
                    disabled={isBusy}
                  >
                    Rimuovi
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Diagnostica</h3>
            {diagnostics && (
              <span className="status-chip status-chip--muted">v{diagnostics.appVersion}</span>
            )}
          </div>
          <div className="settings-form">
            {diagnostics ? (
              <>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Database</span>
                  <code className="diagnostic-value">{diagnostics.databasePath}</code>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Cartella log</span>
                  <code className="diagnostic-value">{diagnostics.logsDirectory}</code>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Ollama</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ollamaIsReachable ? "online" : "offline"}`}
                  >
                    {diagnostics.ollamaStatus}
                  </span>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">OCR ({diagnostics.ocrEngineName})</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ocrIsConfigured ? "online" : "offline"}`}
                  >
                    {diagnostics.ocrStatus}
                  </span>
                </div>
                {!diagnostics.ocrIsConfigured && (
                  <div className="panel-note panel-note--warning" role="alert">
                    <p>{ocrProvisionStatus?.message ?? "OCR non configurato. Configura le dipendenze locali per abilitare OCR."}</p>
                    {ocrProvisionStatus?.lastError && <p>{ocrProvisionStatus.lastError}</p>}
                  </div>
                )}
                {ocrProvisionStatus?.isRunning && (
                  <div className="panel-note" role="status">
                    <p>{ocrProvisionStatus.message}</p>
                  </div>
                )}
              </>
            ) : (
              <div className="panel-note">
                <p>Dati diagnostici non disponibili.</p>
              </div>
            )}
            <div className="settings-actions">
              <button
                type="button"
                className="button-secondary"
                onClick={() => void refreshDiagnostics()}
                disabled={isBusy}
              >
                Aggiorna
              </button>
              <button type="button" onClick={() => void openLogsFolder()} disabled={isBusy}>
                Apri cartella log
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={() => void configureOcrRuntime()}
                disabled={isBusy || Boolean(ocrProvisionStatus?.isRunning)}
              >
                {ocrProvisionStatus?.isRunning ? "Configurazione OCR..." : "Configura OCR"}
              </button>
            </div>
          </div>
        </div>

        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}
      </div>
    </div>
  );
}


