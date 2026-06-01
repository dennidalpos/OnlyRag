export type OllamaSettings = {
  ollamaBaseUrl: string;
  defaultChatModel: string | null;
  defaultEmbeddingModel: string | null;
  defaultTranslationModel: string | null;
  requestTimeoutSeconds: number;
  embeddingBatchSize: number;
  embeddingNumCtx: number | null;
  chatNumCtx: number | null;
  translationNumCtx: number | null;
  trustNonLocalEndpoint: boolean;
};

export type QdrantSettings = {
  grpcEndpoint: string;
  apiKey: string | null;
  trustNonLoopbackEndpoint: boolean;
  requireTlsForRemoteEndpoint: boolean;
  useLocalBundledServer: boolean;
  localGrpcPort: number;
  requestTimeoutSeconds: number;
};

export type QdrantSettingsResponse = Omit<QdrantSettings, "apiKey"> & {
  hasApiKey: boolean;
};

export type OllamaModelDetails = {
  name: string;
  numCtx: number | null;
};

export type OllamaRunningModel = {
  name: string;
  model: string;
  size: number | null;
  sizeVram: number | null;
  digest: string | null;
  contextLength: number | null;
};

export type OfficeConversionSettings = {
  libreOfficePath: string | null;
  conversionTimeoutSeconds: number;
};

export type IngestionSettings = {
  chunkSizeTokens: number;
  overlapTokens: number;
};

export type OcrProcessingSettings = {
  language: string;
  maxRetries: number;
  pageTimeoutSeconds: number;
  lowConfidenceThreshold: number;
};

export type PerformanceSettings = {
  maxParallelJobs: number;
  maxOcrParallelPages: number;
  embeddingBatchSize: number;
  translationBatchSize: number;
  maxContextChunks: number;
  requestTimeoutSeconds: number;
  enableLowResourceMode: boolean;
  profile: PerformanceProfile;
  effectiveProfile: PerformanceProfile;
};

export type PerformanceProfile = "auto" | "eco" | "balanced" | "power" | "custom";

export type OcrSettings = {
  profile: string;
  pdfDpi: number;
  modelPreset: string;
  modelVersion: string;
  detectionSideLimit: number;
  detectionThreshold: number;
  detectionBoxThreshold: number;
  detectionUnclipRatio: number;
  recognitionScoreThreshold: number;
  useTextlineOrientation: boolean;
  useDocumentOrientationClassification: boolean;
  useDocumentUnwarping: boolean;
  recognitionBatchSize: number;
  cpuThreads: number;
  device: string;
};

export type OcrAutoGpuEnableResponse = {
  applied: boolean;
  message: string;
  settings: OcrSettings;
};

export type OfficeConverterStatusResponse = {
  state: string;
  isAvailable: boolean;
  executablePath: string | null;
  message: string;
  suggestion: string | null;
  conversionTimeoutSeconds: number;
};

export type OllamaStatusResponse = {
  state: string;
  isReachable: boolean;
  baseUrl: string;
  installedModelCount: number;
  message: string;
  suggestion: string | null;
  version: string | null;
  runningModels: OllamaRunningModel[] | null;
};

export type OllamaModel = {
  name: string;
  model: string;
  modifiedAt: string | null;
  size: number;
  digest: string | null;
  family: string | null;
  parameterSize: string | null;
  quantizationLevel: string | null;
};

export type OllamaModelsResponse = {
  models: OllamaModel[];
};

export type OperationMessageResponse = {
  message: string;
};

export type OllamaModelPullStartResponse = {
  jobId: string;
  modelName: string;
  status: string;
  message: string;
};

export type OllamaInstallStatus = {
  cliInstalled: boolean;
  apiReachable: boolean;
  installCommand: string;
  networkAccessHint: string;
};

export type DependencyActionResponse = {
  started: boolean;
  message: string;
};

export type OcrProvisionRequest = {
  confirmed: boolean;
  runtimeTarget?: "auto" | "cpu" | "nvidia";
};

export type OcrProvisionStatus = {
  isConfigured: boolean;
  isRunning: boolean;
  message: string;
  lastError: string | null;
  runtimeTarget: string;
  resolvedRuntime: string;
  runtimeDetail: string | null;
  startedAtUtc: string | null;
  updatedAtUtc: string | null;
  stepKey: string | null;
  stepLabel: string | null;
  stepIndex: number;
  stepCount: number;
  progressPercent: number;
  severity: string;
  canRetry: boolean;
  selectedRuntime: string | null;
};

export type OcrStartupAnalysis = {
  shouldPrompt: boolean;
  isWindowsSupported: boolean;
  hasMinimumDiskSpace: boolean;
  availableDiskBytes: number;
  requiredDiskBytes: number;
  hasCompatiblePython: boolean;
  isOcrConfigured: boolean;
  isNvidiaRuntimeAvailable: boolean;
  isGpuUsable: boolean;
  recommendedRuntimeTarget: "auto" | "cpu" | "nvidia";
  title: string;
  message: string;
  findings: string[];
};

export type OcrLanguage = {
  code: string;
  label: string;
  scriptGroup: string;
  isDefault: boolean;
};
