import type {
  IngestionSettings,
  PdfExportSettings,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceProfile,
  PerformanceSettings
} from "../../api";

export const emptySettings: OllamaSettings = {
  ollamaBaseUrl: "http://localhost:11434",
  defaultChatModel: null,
  defaultEmbeddingModel: null,
  defaultTranslationModel: null,
  defaultCodingModel: null,
  requestTimeoutSeconds: 120,
  embeddingBatchSize: 1,
  embeddingNumCtx: null,
  chatNumCtx: null,
  translationNumCtx: null,
  codingNumCtx: null,
  trustNonLocalEndpoint: false
};

export const OLLAMA_MODEL_LIBRARY_URL = "https://ollama.com/library";

export const emptyPdfExportSettings: PdfExportSettings = {
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
  overlapTokens: 120,
  archive: {
    maxFileCount: 1000,
    maxTotalUncompressedBytes: 2 * 1024 * 1024 * 1024,
    maxFileUncompressedBytes: 200 * 1024 * 1024,
    maxDirectoryDepth: 16
  }
};

export const emptyOcrProcessingSettings: OcrProcessingSettings = {
  language: "it",
  maxRetries: 2,
  pageTimeoutSeconds: 180,
  lowConfidenceThreshold: 0.55
};

export const emptyOcrSettings: OcrSettings = {
  profile: "auto",
  pdfDpi: 220,
  detectionSideLimit: 1152,
  detectionThreshold: 0.3,
  recognitionScoreThreshold: 0.5,
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
    recognitionScoreThreshold: 0.58,
    recognitionBatchSize: 4,
    cpuThreads: 1
  },
  balanced: {
    ...emptyOcrSettings,
    profile: "balanced"
  },
  accurate: {
    ...emptyOcrSettings,
    profile: "accurate",
    pdfDpi: 300,
    detectionSideLimit: 1536,
    detectionThreshold: 0.23,
    recognitionScoreThreshold: 0.42,
    useDocumentOrientationClassification: true,
    useDocumentUnwarping: true,
    recognitionBatchSize: 8,
    cpuThreads: 4
  }
};

export function getOcrProfilePreset(profile: string, device: string): OcrSettings | null {
  const targetKey = profile === "auto" ? "balanced" : profile;
  const preset = ocrProfilePresets[targetKey];
  if (!preset) {
    return null;
  }

  const baseSettings = profile === "auto" ? { ...preset, profile: "auto" } : preset;

  if (device !== "gpu") {
    return { ...baseSettings, device: "cpu" };
  }

  const recognitionBatchSize = targetKey === "fast" ? 8 : targetKey === "accurate" ? 16 : 12;
  return { ...baseSettings, device: "gpu", recognitionBatchSize };
}
