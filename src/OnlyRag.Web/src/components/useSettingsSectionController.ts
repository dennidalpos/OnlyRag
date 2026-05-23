import { useEffect, useMemo, useState } from "react";
import {
  apiRequest,
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
  buildNumCtxRecommendation,
  isNonLocalUrl,
  normalizeOllamaSettings,
} from "./SettingsSection.helpers";
import {
  emptyIngestionSettings,
  emptyOcrProcessingSettings,
  emptyOcrSettings,
  emptyOfficeSettings,
  emptyPerformanceSettings,
  emptySettings
} from "./SettingsSection.defaults";
import { createSettingsSectionActions } from "./useSettingsSectionController.actions";

export type SettingsSectionProps = {
  settings: OllamaSettings | null;
  status: OllamaStatusResponse | null;
  models: OllamaModel[];
  loadError: string | null;
  onDataChanged: () => Promise<void>;
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
    void actions.refreshOfficeConverter();
    void actions.refreshPerformanceSettings();
    void actions.refreshIngestionSettings();
    void actions.refreshOcrProcessingSettings();
    void actions.refreshOcrSettings();
    void actions.refreshOcrLanguages();
    void actions.refreshDiagnostics();
    void actions.refreshDependencyStatus();
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
