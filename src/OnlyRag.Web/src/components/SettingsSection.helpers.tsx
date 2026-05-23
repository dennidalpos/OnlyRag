import type {
  IngestionSettings,
  OfficeConversionSettings,
  OcrLanguage,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceProfile,
  PerformanceSettings
} from "../api";
import { clampNumber } from "../numberUtils";
export {
  AdjustableModelContextBar,
  OcrFieldLabel,
  OcrRangeField,
  SettingsRangeField
} from "./SettingsSection.fields";
export {
  formatModelSize,
  formatOcrDecimal,
  formatOcrInteger,
  formatTelemetryBytes,
  formatTelemetryPercent
} from "./SettingsSection.formatting";

export function getOcrSelectOptions(currentValue: string, knownValues: string[]): string[] {
  const current = currentValue.trim();
  const options = new Set(knownValues);
  if (current.length > 0) {
    options.add(current);
  }

  return [...options];
}

export function normalizeOptionalValue(value: string | null): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0 ? null : normalized;
}

export function normalizeOllamaSettings(settings: OllamaSettings): OllamaSettings {
  const ollamaBaseUrl = settings.ollamaBaseUrl.trim();
  return {
    ollamaBaseUrl,
    defaultChatModel: normalizeOptionalValue(settings.defaultChatModel),
    defaultEmbeddingModel: normalizeOptionalValue(settings.defaultEmbeddingModel),
    defaultTranslationModel: normalizeOptionalValue(settings.defaultTranslationModel),
    requestTimeoutSeconds: Number(settings.requestTimeoutSeconds),
    embeddingBatchSize: Number(settings.embeddingBatchSize),
    embeddingNumCtx: settings.embeddingNumCtx != null ? Number(settings.embeddingNumCtx) : null,
    chatNumCtx: settings.chatNumCtx != null ? Number(settings.chatNumCtx) : null,
    translationNumCtx: settings.translationNumCtx != null ? Number(settings.translationNumCtx) : null,
    trustNonLocalEndpoint: isNonLocalUrl(ollamaBaseUrl) && Boolean(settings.trustNonLocalEndpoint)
  };
}

export function normalizeOfficeSettings(settings: OfficeConversionSettings): OfficeConversionSettings {
  return {
    libreOfficePath: normalizeOptionalValue(settings.libreOfficePath),
    conversionTimeoutSeconds: Number(settings.conversionTimeoutSeconds)
  };
}

export function normalizePerformanceSettings(settings: PerformanceSettings): PerformanceSettings {
  const profile = normalizePerformanceProfile(settings.profile, settings.enableLowResourceMode);
  const effectiveProfile = normalizePerformanceProfile(settings.effectiveProfile, false);
  return {
    maxParallelJobs: Number(settings.maxParallelJobs),
    maxOcrParallelPages: Number(settings.maxOcrParallelPages),
    embeddingBatchSize: Number(settings.embeddingBatchSize),
    translationBatchSize: Number(settings.translationBatchSize),
    maxContextChunks: Number(settings.maxContextChunks),
    requestTimeoutSeconds: Number(settings.requestTimeoutSeconds),
    enableLowResourceMode: profile === "eco" || effectiveProfile === "eco",
    profile,
    effectiveProfile
  };
}

export function normalizePerformanceProfile(
  value: string | null | undefined,
  legacyLowResourceMode: boolean
): PerformanceProfile {
  if (legacyLowResourceMode && (!value || value === "auto")) {
    return "eco";
  }

  switch ((value ?? "auto").trim().toLowerCase()) {
    case "eco":
      return "eco";
    case "bilanciato":
    case "balanced":
      return "balanced";
    case "potente":
    case "power":
      return "power";
    case "personalizzato":
    case "custom":
      return "custom";
    default:
      return "auto";
  }
}

export function normalizeIngestionSettings(settings: IngestionSettings): IngestionSettings {
  const chunkSizeTokens = clampNumber(Number(settings.chunkSizeTokens), 100, 4000);
  return {
    chunkSizeTokens,
    overlapTokens: clampNumber(Number(settings.overlapTokens), 0, Math.min(1000, Math.floor(chunkSizeTokens / 2)))
  };
}

export function normalizeOcrProcessingSettings(settings: OcrProcessingSettings): OcrProcessingSettings {
  return {
    language: normalizeOptionalValue(settings.language) ?? "it",
    maxRetries: clampNumber(Number(settings.maxRetries), 0, 2),
    pageTimeoutSeconds: clampNumber(Number(settings.pageTimeoutSeconds), 15, 600),
    lowConfidenceThreshold: clampNumber(Number(settings.lowConfidenceThreshold), 0.01, 0.99)
  };
}

export function normalizeOcrSettings(settings: OcrSettings): OcrSettings {
  return {
    profile: settings.profile.trim(),
    pdfDpi: Number(settings.pdfDpi),
    modelPreset: settings.modelPreset.trim(),
    modelVersion: settings.modelVersion.trim(),
    detectionSideLimit: Number(settings.detectionSideLimit),
    detectionThreshold: Number(settings.detectionThreshold),
    detectionBoxThreshold: Number(settings.detectionBoxThreshold),
    detectionUnclipRatio: Number(settings.detectionUnclipRatio),
    recognitionScoreThreshold: Number(settings.recognitionScoreThreshold),
    useTextlineOrientation: settings.useTextlineOrientation,
    useDocumentOrientationClassification: settings.useDocumentOrientationClassification,
    useDocumentUnwarping: settings.useDocumentUnwarping,
    recognitionBatchSize: Number(settings.recognitionBatchSize),
    cpuThreads: Number(settings.cpuThreads),
    device: settings.device.trim()
  };
}

export function buildOllamaSettingsPayload(
  formState: OllamaSettings,
  performanceFormState: PerformanceSettings
): OllamaSettings {
  return normalizeOllamaSettings({
    ...formState,
    requestTimeoutSeconds: Number(performanceFormState.requestTimeoutSeconds),
    embeddingBatchSize: Number(performanceFormState.embeddingBatchSize)
  });
}

export function buildOfficeSettingsPayload(
  officeFormState: OfficeConversionSettings
): OfficeConversionSettings {
  return normalizeOfficeSettings(officeFormState);
}

export function buildPerformanceSettingsPayload(
  performanceFormState: PerformanceSettings
): PerformanceSettings {
  return normalizePerformanceSettings(performanceFormState);
}

export function buildIngestionSettingsPayload(
  ingestionFormState: IngestionSettings
): IngestionSettings {
  return normalizeIngestionSettings(ingestionFormState);
}

export function buildOcrProcessingSettingsPayload(
  ocrProcessingFormState: OcrProcessingSettings
): OcrProcessingSettings {
  return normalizeOcrProcessingSettings(ocrProcessingFormState);
}

export function buildOcrSettingsPayload(ocrFormState: OcrSettings): OcrSettings {
  return normalizeOcrSettings(ocrFormState);
}

export function areOllamaSettingsEqual(left: OllamaSettings, right: OllamaSettings): boolean {
  return JSON.stringify(normalizeOllamaSettings(left)) === JSON.stringify(normalizeOllamaSettings(right));
}

export function isNonLocalUrl(value: string): boolean {
  try {
    const url = new URL(value.trim());
    return url.hostname !== "localhost"
      && url.hostname !== "127.0.0.1"
      && url.hostname !== "[::1]"
      && url.hostname !== "::1";
  } catch {
    return false;
  }
}

export function areOfficeSettingsEqual(
  left: OfficeConversionSettings,
  right: OfficeConversionSettings
): boolean {
  return JSON.stringify(normalizeOfficeSettings(left)) === JSON.stringify(normalizeOfficeSettings(right));
}

export function arePerformanceSettingsEqual(left: PerformanceSettings, right: PerformanceSettings): boolean {
  return JSON.stringify(normalizePerformanceSettings(left)) === JSON.stringify(normalizePerformanceSettings(right));
}

export function areIngestionSettingsEqual(left: IngestionSettings, right: IngestionSettings): boolean {
  return JSON.stringify(normalizeIngestionSettings(left)) === JSON.stringify(normalizeIngestionSettings(right));
}

export function areOcrProcessingSettingsEqual(left: OcrProcessingSettings, right: OcrProcessingSettings): boolean {
  return JSON.stringify(normalizeOcrProcessingSettings(left)) === JSON.stringify(normalizeOcrProcessingSettings(right));
}

export function areOcrSettingsEqual(left: OcrSettings, right: OcrSettings): boolean {
  return JSON.stringify(normalizeOcrSettings(left)) === JSON.stringify(normalizeOcrSettings(right));
}

export function getOcrLanguageOptions(currentValue: string, languages: OcrLanguage[]): OcrLanguage[] {
  if (languages.length === 0) {
    return [{ code: currentValue || "it", label: currentValue || "it", scriptGroup: "custom", isDefault: false }];
  }

  if (languages.some((language) => language.code === currentValue)) {
    return languages;
  }

  return [
    ...languages,
    { code: currentValue, label: currentValue, scriptGroup: "custom", isDefault: false }
  ];
}

export function buildEmbeddingRecommendations(numCtx: number | null): {
  embeddingNumCtx: number;
  chunkMinimum: number;
  chunkMaximum: number;
} | null {
  if (!numCtx || numCtx <= 0) {
    return null;
  }

  const embeddingNumCtx = clampNumber(Math.round(numCtx / 64) * 64, 64, 131072);
  const chunkMinimum = clampNumber(Math.round(numCtx * 0.1 / 50) * 50, 100, 4000);
  const chunkMaximum = Math.max(
    chunkMinimum,
    clampNumber(Math.round(numCtx * 0.35 / 50) * 50, 100, 4000)
  );

  return { embeddingNumCtx, chunkMinimum, chunkMaximum };
}

export function buildNumCtxRecommendation(numCtx: number | null): number | null {
  if (!numCtx || numCtx <= 0) {
    return null;
  }

  return clampNumber(Math.round(numCtx / 64) * 64, 64, 131072);
}

export function buildContextChunkRecommendation(numCtx: number | null): number | null {
  if (!numCtx || numCtx <= 0) {
    return null;
  }

  return clampNumber(Math.round(numCtx / 1024), 1, 24);
}


