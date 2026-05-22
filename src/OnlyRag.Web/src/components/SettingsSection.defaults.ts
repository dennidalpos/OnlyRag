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

export const ocrProfilePresets: Record<string, OcrSettings> = {
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
