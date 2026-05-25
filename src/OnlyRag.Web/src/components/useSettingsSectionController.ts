import { useEffect, useMemo, useState } from "react";
import {
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
  type OllamaSettings,
  type OllamaStatusResponse,
  type PerformanceSettings
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
  emptyOfficeSettings,
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
  const [officeFormState, setOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
  const [savedOfficeFormState, setSavedOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
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
  const [officeStatus, setOfficeStatus] = useState<OfficeConverterStatusResponse | null>(null);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsResponse | null>(initialDiagnostics);
  const [diagnosticsStatus, setDiagnosticsStatus] = useState<"loading" | "ready" | "unavailable">(
    initialDiagnostics ? "ready" : "loading"
  );
  const [ollamaInstallStatus, setOllamaInstallStatus] = useState<OllamaInstallStatus | null>(null);
  const [ocrProvisionStatus, setOcrProvisionStatus] = useState<OcrProvisionStatus | null>(null);
  const [modelToInstall, setModelToInstall] = useState("");
  const [infoMessage, setInfoMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const { details: embeddingModelDetails, isLoading: embeddingModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultEmbeddingModel
  );
  const { details: chatModelDetails, isLoading: chatModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultChatModel
  );
  const { details: translationModelDetails, isLoading: translationModelDetailsLoading } = useSettingsModelDetails(
    formState.defaultTranslationModel
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
    void actions.refreshOfficeConverter();
    void actions.refreshPerformanceSettings();
    void actions.refreshIngestionSettings();
    void actions.refreshOcrProcessingSettings();
    void actions.refreshOcrSettings();
    void actions.refreshOcrLanguages();
    void actions.refreshDiagnostics();
    void actions.refreshDependencyStatus();
  }, []);

  const installedModelNames = useMemo(() => models.map((model) => model.name), [models]);
  const usesNonLocalOllamaEndpoint = useMemo(() => isNonLocalUrl(formState.ollamaBaseUrl), [formState.ollamaBaseUrl]);
  const unavailableDefaults = useMemo(
    () =>
      [formState.defaultChatModel, formState.defaultEmbeddingModel, formState.defaultTranslationModel].filter(
        (value): value is string => Boolean(value && !installedModelNames.includes(value))
      ),
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
  const {
    hasDirtyOllamaSettings,
    hasDirtyOfficeSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges
  } = useSettingsDirtyState({
    formState,
    savedFormState,
    officeFormState,
    savedOfficeFormState,
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
    setOcrLanguages,
    setOfficeStatus,
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
      void actions.refreshDependencyStatus();
      void actions.refreshDiagnostics();
    }, 3000);

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
    diagnosticsStatus,
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
    saveSettings: actions.saveSettings,
    testConnection: actions.testConnection,
    installModel: actions.installModel,
    removeModel: actions.removeModel,
    openOllamaModelLibrary: actions.openOllamaModelLibrary,
    refreshOfficeConverter: actions.refreshOfficeConverter,
    refreshPerformanceSettings: actions.refreshPerformanceSettings,
    refreshIngestionSettings: actions.refreshIngestionSettings,
    refreshOcrProcessingSettings: actions.refreshOcrProcessingSettings,
    refreshOcrSettings: actions.refreshOcrSettings,
    refreshOcrLanguages: actions.refreshOcrLanguages,
    refreshDiagnostics: actions.refreshDiagnostics,
    refreshDependencyStatus: actions.refreshDependencyStatus,
    installOllama: actions.installOllama,
    openLibreOfficeDownload: actions.openLibreOfficeDownload,
    configureOcrRuntime: actions.configureOcrRuntime,
    cancelOcrRuntimeConfiguration: actions.cancelOcrRuntimeConfiguration,
    openLogsFolder: actions.openLogsFolder,
    saveOfficeSettings: actions.saveOfficeSettings,
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
