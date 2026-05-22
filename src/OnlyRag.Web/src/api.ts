export {
  apiRequest,
  markBackendOffline,
  markBackendOnline,
  resolveBackendBaseUrl,
  resolveBackendBaseUrlDirect,
  resolveBackendErrorMessage,
  resolveBackendSessionToken
} from "./apiClient";
export type { ApiProblemDetails, BackendBridge } from "./apiClient";

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

export type DocumentStatus =
  | "Imported"
  | "Queued"
  | "Processing"
  | "Indexed"
  | "RequiresAdditionalComponent"
  | "Failed";

export type ImportedDocument = {
  id: number;
  documentUid: string;
  originalFileName: string;
  originalPath: string;
  sha256: string | null;
  mimeType: string | null;
  fileExtension: string | null;
  fileSizeBytes: number;
  status: DocumentStatus;
  pageCount: number;
  chunkCount: number;
  currentJobId: string | null;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentEmbeddingStatus = {
  documentId: number;
  state: string;
  model: string | null;
  chunkCount: number;
  embeddedChunkCount: number;
  progressPercent: number;
  currentJobId: string | null;
  currentStep: string | null;
  vectorSearchBackend: string;
  lastEmbeddedAtUtc: string | null;
};

export type DocumentOcrStatus = {
  documentId: number;
  state: string;
  pageCount: number;
  ocrPageCount: number;
  currentPage: number;
  totalPages: number;
  averageConfidence: number | null;
  currentJobId: string | null;
  currentStep: string | null;
  engineName: string | null;
  lastError: string | null;
};

export type DocumentSearchResult = {
  documentId: number;
  documentName: string;
  pageStart: number | null;
  pageEnd: number | null;
  chunkId: number;
  snippet: string;
  score: number;
};

export type DocumentSearchDocumentStatus = {
  documentId: number;
  documentName: string;
  status: DocumentStatus;
  isIndexed: boolean;
  embeddingState: string;
  chunkCount: number;
  embeddedChunkCount: number;
};

export type DocumentSearchResponse = {
  results: DocumentSearchResult[];
  documents: DocumentSearchDocumentStatus[];
  keywordBackend: string;
  vectorBackend: string;
  maxContextCharacters: number;
};

export type ChatRequest = {
  message: string;
  model: string;
  useDocuments: boolean;
  selectedDocumentIds: number[];
  conversationId: string | null;
};

export type ChatSource = {
  documentId: number;
  documentName: string;
  pageStart: number | null;
  pageEnd: number | null;
  chunkId: number;
  snippet: string;
  score: number;
};

export type ChatNotice = {
  code: string;
  message: string;
};

export type ChatResponse = {
  conversationId: string;
  model: string;
  answer: string;
  usedDocuments: boolean;
  sources: ChatSource[];
  notices: ChatNotice[];
};

export type CreateTranslationRequest = {
  documentId: number;
  targetLanguage: string;
  model: string;
};

export type TranslationSummary = {
  id: number;
  documentId: number;
  documentName: string;
  sourceLanguage: string;
  targetLanguage: string;
  model: string;
  status: string;
  jobId: string | null;
  unitCount: number;
  completedUnitCount: number;
  progressPercent: number;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TranslationUnit = {
  id: number;
  translationId: number;
  unitIndex: number;
  unitKind: string;
  pageNumber: number | null;
  sourceText: string;
  machineTranslatedText: string | null;
  translatedText: string | null;
  status: string;
  manuallyEdited: boolean;
  validationWarnings: string | null;
  error: string | null;
  attemptCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TranslationDetail = {
  translation: TranslationSummary;
  units: TranslationUnit[];
};

export type TranslationCompare = {
  translation: TranslationSummary;
  currentPage: number;
  pagePosition: number;
  pageCount: number;
  previousPage: number | null;
  nextPage: number | null;
  units: TranslationUnit[];
};

export type TranslationExport = {
  translationId: number;
  format: string;
  outputPath: string;
  status: string;
};

export type JobStatus = "Pending" | "Running" | "Pausing" | "Completed" | "Failed" | "Cancelled" | "Paused";

export type LocalJob = {
  id: string;
  type: string;
  status: JobStatus;
  priority: number;
  progressPercent: number;
  currentStep: string;
  error: string | null;
  updatedAt: string;
};

export type DocumentImportResult = {
  document: ImportedDocument;
  deduplicated: boolean;
  message: string;
};

export type DocumentImportFileResult = {
  fileName: string;
  document: ImportedDocument | null;
  deduplicated: boolean;
  succeeded: boolean;
  message: string;
  errorCode: string | null;
};

export type DocumentImportResponse = {
  documents: DocumentImportResult[];
  results: DocumentImportFileResult[];
  hasFailures: boolean;
};

export type DiagnosticsResponse = {
  appVersion: string;
  databasePath: string;
  logsDirectory: string;
  ollamaStatus: string;
  ollamaIsReachable: boolean;
  ocrStatus: string;
  ocrIsConfigured: boolean;
  ocrEngineName: string;
  ocrGpuCapability: OcrGpuCapability;
  systemTelemetry: SystemTelemetry;
};

export type OcrGpuCapability = {
  isUsable: boolean;
  status: string;
  blockReason: string | null;
  runtimeDetail: string | null;
  engineVersion: string | null;
  nvidiaName: string | null;
  driverVersion: string | null;
  compiledWithCuda: boolean | null;
  cudaDeviceCount: number | null;
  activeDevice: string | null;
  packageVersions: Record<string, string>;
};

export type SystemTelemetry = {
  cpu: CpuTelemetry;
  memory: MemoryTelemetry;
  systemDisk: DiskTelemetry;
  gpu: GpuTelemetry | null;
};

export type CpuTelemetry = {
  logicalProcessorCount: number;
  usagePercent: number | null;
};

export type MemoryTelemetry = {
  totalBytes: number;
  availableBytes: number;
};

export type DiskTelemetry = {
  name: string;
  totalBytes: number;
  availableBytes: number;
};

export type GpuTelemetry = {
  name: string;
  driverVersion: string;
  usagePercent: number | null;
  memoryTotalBytes: number | null;
  memoryAvailableBytes: number | null;
};

export type VectorBackendHealth = {
  backendName: string;
  storagePersistent: boolean;
  vectorLimit: number;
  totalVectors: number;
  nearLimit: boolean;
  warning: string | null;
};

export type OcrPolicy = "Auto" | "ForceAll";

export type PhaseState = "Todo" | "InProgress" | "Completed" | "Skipped" | "Failed" | "Obsolete";

export type PipelinePhaseInfo = {
  state: PhaseState;
  error: string | null;
  completedAtUtc: string | null;
};

export type DocumentPipelineStatus = {
  documentId: number;
  ocrPolicy: string;
  import: PipelinePhaseInfo;
  analysis: PipelinePhaseInfo;
  ocr: PipelinePhaseInfo;
  textExtraction: PipelinePhaseInfo;
  chunking: PipelinePhaseInfo;
  embedding: PipelinePhaseInfo;
  overallState: PhaseState;
  activeJobId: string | null;
  activeJobType: string | null;
};

export type DocumentPageInfo = {
  pageNumber: number;
  textContent: string | null;
  ocrStatus: string | null;
  ocrEngine: string | null;
  ocrConfidence: number | null;
  ocrError: string | null;
};

export type DocumentPreviewResponse = {
  documentId: number;
  originalFileName: string;
  mimeType: string | null;
  fileExtension: string | null;
  fileSizeBytes: number;
  pageCount: number;
  chunkCount: number;
  status: string;
  pageStart: number;
  pageSize: number;
  returnedPageCount: number;
  pages: DocumentPageInfo[];
};
