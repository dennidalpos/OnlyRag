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

export type OllamaModelDetails = {
  name: string;
  numCtx: number | null;
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
  recommendedRuntimeTarget: "cpu" | "nvidia";
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
