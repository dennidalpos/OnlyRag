import type {
  DiagnosticsResponse,
  DocumentEmbeddingStatus,
  DocumentOcrStatus,
  DocumentPipelineStatus,
  ImportedDocument,
  LocalJob,
  OcrLanguage,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse,
  PerformanceSettings,
  QdrantStatusResponse,
  RerankerModelInfo,
  VectorBackendHealth
} from "../api";

export function createDocument(overrides: Partial<ImportedDocument> = {}): ImportedDocument {
  return {
    id: 1,
    documentUid: "doc-1",
    originalFileName: "manuale.pdf",
    originalPath: "C:\\Docs\\manuale.pdf",
    sha256: "abc123",
    mimeType: "application/pdf",
    fileExtension: ".pdf",
    fileSizeBytes: 2048,
    status: "Indexed",
    pageCount: 2,
    chunkCount: 4,
    currentJobId: null,
    lastError: null,
    createdAtUtc: "2026-05-21T12:00:00Z",
    updatedAtUtc: "2026-05-21T12:05:00Z",
    ...overrides
  };
}

export function createModel(overrides: Partial<OllamaModel> = {}): OllamaModel {
  return {
    name: "llama3.2:3b",
    model: "llama3.2:3b",
    modifiedAt: "2026-05-21T12:00:00Z",
    size: 2_000_000_000,
    digest: "digest",
    family: "llama",
    parameterSize: "3B",
    quantizationLevel: "Q4",
    ...overrides
  };
}

export function createOllamaSettings(overrides: Partial<OllamaSettings> = {}): OllamaSettings {
  return {
    ollamaBaseUrl: "http://localhost:11434",
    defaultChatModel: "llama3.2:3b",
    defaultEmbeddingModel: "nomic-embed-text",
    defaultTranslationModel: "llama3.2:3b",
    defaultCodingModel: "qwen2.5-coder",
    requestTimeoutSeconds: 120,
    embeddingBatchSize: 1,
    embeddingNumCtx: null,
    chatNumCtx: null,
    translationNumCtx: null,
    codingNumCtx: null,
    trustNonLocalEndpoint: false,
    ...overrides
  };
}

export function createOllamaStatus(overrides: Partial<OllamaStatusResponse> = {}): OllamaStatusResponse {
  return {
    state: "Ready",
    isReachable: true,
    baseUrl: "http://localhost:11434",
    installedModelCount: 1,
    message: "Ollama raggiungibile.",
    suggestion: null,
    version: null,
    runningModels: [],
    ...overrides
  };
}

export function createVectorHealth(overrides: Partial<VectorBackendHealth> = {}): VectorBackendHealth {
  return {
    backendName: "Qdrant gRPC",
    storagePersistent: true,
    vectorLimit: 100000,
    totalVectors: 4,
    nearLimit: false,
    warning: null,
    ...overrides
  };
}

export function createQdrantStatus(overrides: Partial<QdrantStatusResponse> = {}): QdrantStatusResponse {
  return {
    status: "Online",
    isReachable: true,
    grpcEndpoint: "http://127.0.0.1:6334",
    isLoopback: true,
    isTls: false,
    hasApiKey: false,
    version: "1.18.1",
    binaryPath: "C:\\OnlyRag\\qdrant\\qdrant.exe",
    configPath: "C:\\OnlyRag\\qdrant\\config\\qdrant.yaml",
    storageDirectory: "C:\\OnlyRag\\qdrant\\storage",
    processId: 1234,
    warning: null,
    error: null,
    ...overrides
  };
}

export function createOcrLanguage(overrides: Partial<OcrLanguage> = {}): OcrLanguage {
  return {
    code: "it",
    label: "Italiano",
    scriptGroup: "Principali",
    isDefault: true,
    ...overrides
  };
}

export function createJob(overrides: Partial<LocalJob> = {}): LocalJob {
  return {
    id: "job-1",
    type: "document-ingestion",
    status: "Running",
    priority: 0,
    progressPercent: 45,
    currentStep: "Importazione in corso",
    payloadJson: "{}",
    checkpointJson: "{}",
    error: null,
    retryCount: 0,
    maxRetries: 3,
    nextAttemptAtUtc: null,
    createdAt: "2026-05-21T12:00:00Z",
    updatedAt: "2026-05-21T12:10:00Z",
    ...overrides
  };
}

export function createEmbeddingStatus(overrides: Partial<DocumentEmbeddingStatus> = {}): DocumentEmbeddingStatus {
  return {
    documentId: 1,
    state: "Completed",
    model: "nomic-embed-text",
    chunkCount: 4,
    embeddedChunkCount: 4,
    progressPercent: 100,
    currentJobId: null,
    currentStep: null,
    vectorSearchBackend: "Qdrant gRPC",
    lastEmbeddedAtUtc: "2026-05-21T12:05:00Z",
    ...overrides
  };
}

export function createOcrStatus(overrides: Partial<DocumentOcrStatus> = {}): DocumentOcrStatus {
  return {
    documentId: 1,
    state: "Completed",
    pageCount: 2,
    ocrPageCount: 2,
    currentPage: 2,
    totalPages: 2,
    averageConfidence: 0.92,
    currentJobId: null,
    currentStep: null,
    engineName: "PaddleOCR",
    lastError: null,
    ...overrides
  };
}

export function createPipelineStatus(overrides: Partial<DocumentPipelineStatus> = {}): DocumentPipelineStatus {
  const completed = { state: "Completed" as const, error: null, completedAtUtc: "2026-05-21T12:05:00Z" };
  return {
    documentId: 1,
    ocrPolicy: "Auto",
    import: completed,
    analysis: completed,
    ocr: completed,
    textExtraction: completed,
    chunking: completed,
    embedding: completed,
    overallState: "Completed",
    activeJobId: null,
    activeJobType: null,
    ...overrides
  };
}

export function createDiagnostics(overrides: Partial<DiagnosticsResponse> = {}): DiagnosticsResponse {
  return {
    appVersion: "0.1.0",
    databasePath: "C:\\OnlyRag\\onlyrag.db",
    logsDirectory: "C:\\OnlyRag\\logs",
    ollamaStatus: "Ready",
    ollamaIsReachable: true,
    ollamaVersion: null,
    ollamaRunningModels: [],
    qdrant: createQdrantStatus(),
    ocrStatus: "Configured",
    ocrIsConfigured: true,
    ocrEngineName: "PaddleOCR",
    ocrGpuCapability: {
      isUsable: false,
      status: "NVIDIA non disponibile",
      blockReason: "nvidia-smi non trovato.",
      runtimeDetail: null,
      engineVersion: null,
      nvidiaName: null,
      driverVersion: null,
      compiledWithCuda: null,
      cudaDeviceCount: null,
      activeDevice: null,
      packageVersions: {},
      capabilityStatus: "no_nvidia_gpu"
    },
    systemTelemetry: {
      cpu: {
        logicalProcessorCount: 8,
        usagePercent: 12.5
      },
      memory: {
        totalBytes: 16 * 1024 * 1024 * 1024,
        availableBytes: 9 * 1024 * 1024 * 1024
      },
      systemDisk: {
        name: "C:\\",
        totalBytes: 512 * 1024 * 1024 * 1024,
        availableBytes: 240 * 1024 * 1024 * 1024
      },
      gpu: null
    },
    imageGeneration: {
      state: "Ready",
      isReady: true,
      executionProvider: "DirectML",
      message: "Provider integrato pronto con DirectML.",
      suggestion: null,
      preferredExecutionProvider: "DirectML",
      modelState: "Verified",
      fallbackReason: null
    },
    reranker: {
      isDownloaded: true,
      isDownloading: false,
      statusText: "ONNX Cross-Encoder"
    },
    cloudLlm: {
      provider: "OpenAI",
      hasApiKey: false,
      statusText: "Non configurato"
    },
    modules: [
      { module: "Qdrant", state: "online", durationMs: 10, error: null },
      { module: "Database & FTS5", state: "online", durationMs: 10, error: null },
      { module: "Agent Engine", state: "online", durationMs: 10, error: null },
      { module: "Knowledge Graph", state: "online", durationMs: 10, error: null }
    ],
    ...overrides
  };
}

export function createPerformanceSettings(overrides: Partial<PerformanceSettings> = {}): PerformanceSettings {
  return {
    maxParallelJobs: 1,
    maxOcrParallelPages: 1,
    embeddingBatchSize: 1,
    translationBatchSize: 1,
    maxContextChunks: 8,
    requestTimeoutSeconds: 120,
    enableLowResourceMode: false,
    profile: "auto",
    effectiveProfile: "balanced",
    ...overrides
  };
}

export function createOllamaInstallStatus(overrides: Partial<OllamaInstallStatus> = {}): OllamaInstallStatus {
  return {
    cliInstalled: true,
    apiReachable: true,
    installCommand: "https://ollama.com/download",
    networkAccessHint: "Download manuale disponibile.",
    ...overrides
  };
}

export function createRerankerModelInfo(overrides: Partial<RerankerModelInfo> = {}): RerankerModelInfo {
  return {
    id: "bge-reranker-base",
    name: "BGE Re-Ranker Base (ONNX)",
    description: "Neural 2nd-stage Cross-Encoder re-ranker model.",
    modelFileName: "bge-reranker-base.onnx",
    downloadUrl: "https://huggingface.co/BAAI/bge-reranker-base/resolve/main/onnx/model.onnx",
    fileSizeBytes: 560000000,
    sha256Hash: "",
    isDownloaded: false,
    downloadProgress: 0.0,
    isDownloading: false,
    downloadError: null,
    ...overrides
  };
}
