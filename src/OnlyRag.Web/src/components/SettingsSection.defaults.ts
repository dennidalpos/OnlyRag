import type {
  IngestionSettings,
  OfficeConversionSettings,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceSettings
} from "../api";

export const emptySettings: OllamaSettings = {
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

export const OLLAMA_MODEL_LIBRARY_URL = "https://ollama.com/library";

export const emptyOfficeSettings: OfficeConversionSettings = {
  libreOfficePath: null,
  conversionTimeoutSeconds: 120
};

export const emptyPerformanceSettings: PerformanceSettings = {
  maxParallelJobs: 1,
  maxOcrParallelPages: 1,
  embeddingBatchSize: 1,
  translationBatchSize: 1,
  maxContextChunks: 8,
  requestTimeoutSeconds: 120,
  enableLowResourceMode: false
};

export const emptyIngestionSettings: IngestionSettings = {
  chunkSizeTokens: 800,
  overlapTokens: 120
};

export const emptyOcrProcessingSettings: OcrProcessingSettings = {
  language: "it",
  maxRetries: 2,
  pageTimeoutSeconds: 180,
  lowConfidenceThreshold: 0.55
};

export const emptyOcrSettings: OcrSettings = {
  profile: "balanced",
  pdfDpi: 220,
  modelPreset: "PP-OCRv5",
  modelVersion: "PP-OCRv5",
  detectionSideLimit: 1152,
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

export const ocrProfilePresets: Record<string, OcrSettings> = {
  fast: {
    ...emptyOcrSettings,
    profile: "fast",
    pdfDpi: 160,
    detectionSideLimit: 896,
    detectionThreshold: 0.38,
    detectionBoxThreshold: 0.68,
    detectionUnclipRatio: 1.35,
    recognitionScoreThreshold: 0.58,
    recognitionBatchSize: 4,
    cpuThreads: 1
  },
  balanced: emptyOcrSettings,
  accurate: {
    ...emptyOcrSettings,
    profile: "accurate",
    pdfDpi: 300,
    detectionSideLimit: 1536,
    detectionThreshold: 0.23,
    detectionBoxThreshold: 0.52,
    detectionUnclipRatio: 1.75,
    recognitionScoreThreshold: 0.42,
    useDocumentOrientationClassification: true,
    useDocumentUnwarping: true,
    recognitionBatchSize: 8,
    cpuThreads: 4
  }
};
