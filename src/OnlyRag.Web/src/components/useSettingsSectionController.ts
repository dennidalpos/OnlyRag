import { useEffect, useMemo, useState } from "react";
import {
  apiRequest,
  type DiagnosticsResponse,
  type IngestionSettings,
  type LocalJob,
  type PdfExportSettings,
  type PdfExportConverterStatusResponse,
  type OcrLanguage,
  type OcrProcessingSettings,
  type OcrProvisionStatus,
  type OcrSettings,
  type OllamaInstallStatus,
  type OllamaModel,
  type OllamaSettings,
  type OllamaStatusResponse,
  type PerformanceSettings,
  type RerankerModelInfo
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import {
  buildContextChunkRecommendation,
  buildEmbeddingRecommendations,
  buildNumCtxRecommendation,
  isNonLocalUrl,
  normalizeOllamaSettings
} from "./SettingsSection.helpers";
import {
  emptyIngestionSettings,
  emptyOcrProcessingSettings,
  emptyOcrSettings,
  emptyPdfExportSettings,
  emptyPerformanceSettings,
  emptySettings
} from "./SettingsSection.defaults";
import { useSettingsDirtyState } from "./useSettingsDirtyState";
import { createSettingsSectionActions } from "./useSettingsSectionController.actions";
import { useSettingsModelDetails } from "./useSettingsModelDetails";

export type SettingsSectionProps = {
  settings: OllamaSettings | null;
  status: OllamaStatusResponse | null;
  models: OllamaModel[];
  initialDiagnostics?: DiagnosticsResponse | null;
  loadError: string | null;
  onDataChanged: () => Promise<void>;
};

export function useSettingsSectionController({
  settings,
  status,
  models,
  initialDiagnostics = null,
  loadError,
  onDataChanged
}: SettingsSectionProps) {
  const [formState, setFormState] = useState<OllamaSettings>(emptySettings);
  const [savedFormState, setSavedFormState] = useState<OllamaSettings>(emptySettings);
  const [pdfExportFormState, setPdfExportFormState] = useState<PdfExportSettings>(emptyPdfExportSettings);
  const [savedPdfExportFormState, setSavedPdfExportFormState] = useState<PdfExportSettings>(emptyPdfExportSettings);
  const [performanceFormState, setPerformanceFormState] = useState<PerformanceSettings>(emptyPerformanceSettings);
  const [savedPerformanceFormState, setSavedPerformanceFormState] =
    useState<PerformanceSettings>(emptyPerformanceSettings);
  const [ingestionFormState, setIngestionFormState] = useState<IngestionSettings>(emptyIngestionSettings);
  const [savedIngestionFormState, setSavedIngestionFormState] = useState<IngestionSettings>(emptyIngestionSettings);
  const [ocrProcessingFormState, setOcrProcessingFormState] =
    useState<OcrProcessingSettings>(emptyOcrProcessingSettings);
  const [savedOcrProcessingFormState, setSavedOcrProcessingFormState] =
    useState<OcrProcessingSettings>(emptyOcrProcessingSettings);
  const [ocrFormState, setOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [savedOcrFormState, setSavedOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [ocrLanguages, setOcrLanguages] = useState<OcrLanguage[]>([]);
  const [pdfExportStatus, setPdfExportStatus] = useState<PdfExportConverterStatusResponse | null>(null);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsResponse | null>(initialDiagnostics);
  const [diagnosticsStatus, setDiagnosticsStatus] = useState<"loading" | "ready" | "unavailable">(
    initialDiagnostics ? "ready" : "loading"
  );
  const [ollamaInstallStatus, setOllamaInstallStatus] = useState<OllamaInstallStatus | null>(null);
  const [ocrProvisionStatus, setOcrProvisionStatus] = useState<OcrProvisionStatus | null>(null);
  const [rerankerModelInfo, setRerankerModelInfo] = useState<RerankerModelInfo | null>(null);
  const [modelToInstall, setModelToInstall] = useState("");
  const [modelPullJobs, setModelPullJobs] = useState<LocalJob[]>([]);
  const [infoMessage, setInfoMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);

  async function refreshRerankerModelInfo() {
    try {
      const info = await apiRequest<RerankerModelInfo>("/api/rag/reranker/model");
      setRerankerModelInfo(info);
      return info;
    } catch {
      setRerankerModelInfo(null);
      return null;
    }
  }

  async function downloadRerankerModel() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);
    try {
      await apiRequest<{ success: boolean }>("/api/rag/reranker/download", {
        method: "POST"
      });
      setInfoMessage("Download del modello ONNX Re-Ranker avviato.");
      await refreshRerankerModelInfo();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile avviare il download del modello ONNX Re-Ranker.");
    } finally {
      setIsBusy(false);
    }
  }

  async function cancelRerankerDownload() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);
    try {
      await apiRequest<{ success: boolean }>("/api/rag/reranker/download", {
        method: "DELETE"
      });
      setInfoMessage("Download del modello ONNX Re-Ranker annullato.");
      await refreshRerankerModelInfo();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile annullare il download del modello.");
    } finally {
      setIsBusy(false);
    }
  }

  async function deleteRerankerModel() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);
    try {
      await apiRequest<{ deleted: boolean }>("/api/rag/reranker/model", {
        method: "DELETE"
      });
      setInfoMessage("Modello ONNX Re-Ranker eliminato.");
      await refreshRerankerModelInfo();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile eliminare il modello ONNX Re-Ranker.");
    } finally {
      setIsBusy(false);
    }
  }
  const { details: embeddingModelDetails, isLoading: embeddingModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultEmbeddingModel
  );
  const { details: chatModelDetails, isLoading: chatModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultChatModel
  );
  const { details: translationModelDetails, isLoading: translationModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultTranslationModel
  );
  const { details: codingModelDetails, isLoading: codingModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultCodingModel
  );

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
    if (initialDiagnostics) {
      setDiagnostics(initialDiagnostics);
      setDiagnosticsStatus("ready");
    }
  }, [initialDiagnostics]);

  useEffect(() => {
    void actions.refreshPdfExportConverter();
    void actions.refreshPerformanceSettings();
    void actions.refreshIngestionSettings();
    void actions.refreshOcrProcessingSettings();
    void actions.refreshOcrSettings();
    void actions.refreshOcrLanguages();
    void actions.refreshDiagnostics();
    void actions.refreshDependencyStatus();
    void refreshRerankerModelInfo();
  }, []);

  useEffect(() => {
    if (!rerankerModelInfo?.isDownloading) {
      return;
    }

    const interval = window.setInterval(() => {
      void refreshRerankerModelInfo();
    }, 2000);

    return () => window.clearInterval(interval);
  }, [rerankerModelInfo?.isDownloading]);

  const installedModelNames = useMemo(() => models.map((model) => model.name), [models]);
  const usesNonLocalOllamaEndpoint = useMemo(() => isNonLocalUrl(formState.ollamaBaseUrl), [formState.ollamaBaseUrl]);
  const unavailableDefaults = useMemo(
    () =>
      [
        formState.defaultChatModel,
        formState.defaultEmbeddingModel,
        formState.defaultTranslationModel,
        formState.defaultCodingModel
      ].filter(
        (value): value is string => Boolean(value && !installedModelNames.includes(value))
      ),
    [
      formState.defaultChatModel,
      formState.defaultEmbeddingModel,
      formState.defaultTranslationModel,
      formState.defaultCodingModel,
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
  const codingNumCtxRecommendation = useMemo(
    () => buildNumCtxRecommendation(codingModelDetails?.numCtx ?? null),
    [codingModelDetails]
  );
  const recommendedMaxContextChunks = useMemo(
    () => buildContextChunkRecommendation(chatModelDetails?.numCtx ?? embeddingModelDetails?.numCtx ?? null),
    [chatModelDetails, embeddingModelDetails]
  );
  const {
    hasDirtyOllamaSettings,
    hasDirtyPdfExportSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges
  } = useSettingsDirtyState({
    formState,
    savedFormState,
    pdfExportFormState,
    savedPdfExportFormState,
    performanceFormState,
    savedPerformanceFormState,
    ingestionFormState,
    savedIngestionFormState,
    ocrProcessingFormState,
    savedOcrProcessingFormState,
    ocrFormState,
    savedOcrFormState
  });

  const actions = createSettingsSectionActions({
    onDataChanged,
    modelToInstall,
    formState,
    pdfExportFormState,
    performanceFormState,
    ingestionFormState,
    ocrProcessingFormState,
    ocrFormState,
    hasDirtyPerformanceSettings,
    hasDirtyOllamaSettings,
    hasDirtyPdfExportSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
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
    setOcrLanguages,
    setPdfExportStatus,
    setDiagnostics,
    setDiagnosticsStatus,
    setOllamaInstallStatus,
    setOcrProvisionStatus,
    setModelToInstall,
    setInfoMessage,
    setErrorMessage,
    setIsBusy
  });

  useEffect(() => {
    setExitContributor("settings", {
      label: "Impostazioni",
      hasPendingChanges,
      hasActiveWork: isBusy,
      prepareForExit: actions.persistAllDirtyChanges
    });

    return () => {
      clearExitContributor("settings");
    };
  }, [
    formState,
    hasDirtyIngestionSettings,
    hasDirtyPdfExportSettings,
    hasDirtyOllamaSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasDirtyPerformanceSettings,
    hasPendingChanges,
    isBusy,
    ingestionFormState,
    ocrFormState,
    ocrProcessingFormState,
    pdfExportFormState,
    performanceFormState
  ]);

  useEffect(() => {
    if (!ocrProvisionStatus?.isRunning) {
      return;
    }

    const interval = window.setInterval(() => {
      void actions.refreshDependencyStatus();
      void actions.refreshDiagnostics();
    }, 3000);

    return () => window.clearInterval(interval);
  }, [ocrProvisionStatus?.isRunning]);

  useEffect(() => {
    let isCancelled = false;
    let sawActivePull = false;

    async function pollModelPullJobs() {
      try {
        const jobs = await apiRequest<LocalJob[]>("/api/jobs?limit=100");
        if (isCancelled) {
          return;
        }

        const pullJobs = jobs.filter((job) => job.type === "ollama-model-pull");
        const hasActivePull = pullJobs.some((job) =>
          job.status === "Pending" || job.status === "Running" || job.status === "Pausing" || job.status === "Paused"
        );
        if (sawActivePull && !hasActivePull) {
          void onDataChanged();
        }

        sawActivePull = hasActivePull;
        setModelPullJobs(pullJobs);
      } catch {
        if (!isCancelled) {
          setModelPullJobs([]);
        }
      }
    }

    void pollModelPullJobs();
    const interval = window.setInterval(() => void pollModelPullJobs(), 3000);
    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, [onDataChanged]);

  return {
    settings,
    status,
    models,
    loadError,
    formState,
    setFormState,
    pdfExportFormState,
    setPdfExportFormState,
    performanceFormState,
    setPerformanceFormState,
    ingestionFormState,
    setIngestionFormState,
    ocrProcessingFormState,
    setOcrProcessingFormState,
    ocrFormState,
    modelToInstall,
    setModelToInstall,
    modelPullJobs,
    pdfExportStatus,
    diagnostics,
    diagnosticsStatus,
    ollamaInstallStatus,
    ocrProvisionStatus,
    rerankerModelInfo,
    ocrLanguages,
    embeddingModelDetails,
    chatModelDetails,
    translationModelDetails,
    codingModelDetails,
    embeddingModelDetailsLoading,
    chatModelDetailsLoading,
    translationModelDetailsLoading,
    codingModelDetailsLoading,
    usesNonLocalOllamaEndpoint,
    unavailableDefaults,
    embeddingRecommendations,
    chatNumCtxRecommendation,
    translationNumCtxRecommendation,
    codingNumCtxRecommendation,
    recommendedMaxContextChunks,
    hasDirtyOllamaSettings,
    hasDirtyPdfExportSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges,
    infoMessage,
    errorMessage,
    isBusy,
    saveSettings: actions.saveSettings,
    testConnection: actions.testConnection,
    installModel: actions.installModel,
    removeModel: actions.removeModel,
    openOllamaModelLibrary: actions.openOllamaModelLibrary,
    refreshPdfExportConverter: actions.refreshPdfExportConverter,
    refreshPerformanceSettings: actions.refreshPerformanceSettings,
    refreshIngestionSettings: actions.refreshIngestionSettings,
    refreshOcrProcessingSettings: actions.refreshOcrProcessingSettings,
    refreshOcrSettings: actions.refreshOcrSettings,
    refreshOcrLanguages: actions.refreshOcrLanguages,
    refreshDiagnostics: actions.refreshDiagnostics,
    refreshDependencyStatus: actions.refreshDependencyStatus,
    refreshRerankerModelInfo,
    downloadRerankerModel,
    cancelRerankerDownload,
    deleteRerankerModel,
    installOllama: actions.installOllama,
    openLibreOfficeDownload: actions.openLibreOfficeDownload,
    configureOcrRuntime: actions.configureOcrRuntime,
    cancelOcrRuntimeConfiguration: actions.cancelOcrRuntimeConfiguration,
    openLogsFolder: actions.openLogsFolder,
    savePdfExportSettings: actions.savePdfExportSettings,
    savePerformanceSettings: actions.savePerformanceSettings,
    saveIngestionSettings: actions.saveIngestionSettings,
    saveOcrProcessingSettings: actions.saveOcrProcessingSettings,
    applyOcrProfile: actions.applyOcrProfile,
    updateOcrSettings: actions.updateOcrSettings,
    saveOcrSettings: actions.saveOcrSettings,
    restoreBalancedDefaults: actions.restoreBalancedDefaults,
    requestAppDataReset: actions.requestAppDataReset
  } as const;
}

export type SettingsSectionController = ReturnType<typeof useSettingsSectionController>;
