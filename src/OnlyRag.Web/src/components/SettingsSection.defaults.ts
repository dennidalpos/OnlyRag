import type {
  IngestionSettings,
  OfficeConversionSettings,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceProfile,
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
  enableLowResourceMode: false,
  profile: "auto",
  effectiveProfile: "eco"
};

export const performanceProfilePresets: Record<Exclude<PerformanceProfile, "auto" | "custom">, PerformanceSettings> = {
  eco: {
    ...emptyPerformanceSettings,
    profile: "eco",
    effectiveProfile: "eco",
    enableLowResourceMode: true,
    maxParallelJobs: 1,
    maxOcrParallelPages: 1,
    embeddingBatchSize: 1,
    translationBatchSize: 1,
    maxContextChunks: 6,
    requestTimeoutSeconds: 180
  },
  balanced: {
    ...emptyPerformanceSettings,
    profile: "balanced",
    effectiveProfile: "balanced",
    maxParallelJobs: 2,
    maxOcrParallelPages: 2,
    embeddingBatchSize: 2,
    translationBatchSize: 1,
    maxContextChunks: 8,
    requestTimeoutSeconds: 120
  },
  power: {
    ...emptyPerformanceSettings,
    profile: "power",
    effectiveProfile: "power",
    maxParallelJobs: 4,
    maxOcrParallelPages: 4,
    embeddingBatchSize: 4,
    translationBatchSize: 2,
    maxContextChunks: 12,
    requestTimeoutSeconds: 120
  }
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

export function getOcrProfilePreset(profile: string, device: string): OcrSettings | null {
  const preset = ocrProfilePresets[profile];
  if (!preset) {
    return null;
  }

  if (device !== "gpu") {
    return { ...preset, device: "cpu" };
  }

  const recognitionBatchSize = profile === "fast" ? 8 : profile === "accurate" ? 16 : 12;
  return { ...preset, device: "gpu", recognitionBatchSize };
}
