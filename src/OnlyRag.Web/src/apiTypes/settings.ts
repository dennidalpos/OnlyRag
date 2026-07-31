export type OllamaSettings = {
  ollamaBaseUrl: string;
  defaultChatModel: string | null;
  defaultEmbeddingModel: string | null;
  defaultTranslationModel: string | null;
  requestTimeoutSeconds: number;
  embeddingBatchSize: number;
  defaultCodingModel: string | null;
  embeddingNumCtx: number | null;
  chatNumCtx: number | null;
  translationNumCtx: number | null;
  codingNumCtx: number | null;
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

export type PdfExportSettings = {
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
  detectionSideLimit: number;
  detectionThreshold: number;
  recognitionScoreThreshold: number;
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

export type PdfExportConverterStatusResponse = {
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

export type AppLogLevel = "Trace" | "Debug" | "Information" | "Warning" | "Error" | "None";

export type LoggingSettings = {
  minLevel: AppLogLevel;
};

export type LogEntry = {
  id: string;
  timestampUtc: string;
  level: AppLogLevel;
  category: string;
  message: string;
  exceptionDetails: string | null;
  dataJson: string | null;
};

export type LogStorageInfo = {
  totalSizeBytes: number;
  formattedSize: string;
  memoryEntryCount: number;
  fileCount: number;
  logDirectory: string;
};

export type OcrLanguage = {
  code: string;
  label: string;
  scriptGroup: string;
  isDefault: boolean;
};

export type RerankerModelInfo = {
  id: string;
  name: string;
  description: string;
  modelFileName: string;
  downloadUrl: string;
  fileSizeBytes: number;
  sha256Hash: string;
  isDownloaded: boolean;
  downloadProgress: number;
  isDownloading: boolean;
  downloadError: string | null;
};

