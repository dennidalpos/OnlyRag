import type {
  IngestionSettings,
  OfficeConversionSettings,
  OcrLanguage,
  OcrProcessingSettings,
  OcrSettings,
  OllamaModelDetails,
  OllamaSettings,
  PerformanceSettings
} from "../api";
type OcrRangeFieldProps = {
  id: string;
  label: string;
  tooltip: string;
  min: number;
  max: number;
  step?: number;
  value: number;
  formatValue?: (value: number) => string;
  onChange: (value: number) => void;
};

type SettingsRangeFieldProps = {
  id: string;
  label: string;
  min: number;
  max: number;
  step?: number;
  value: number;
  disabled?: boolean;
  hint?: string | null;
  formatValue?: (value: number) => string;
  onChange: (value: number) => void;
};

export function SettingsRangeField({
  id,
  label,
  min,
  max,
  step = 1,
  value,
  disabled = false,
  hint = null,
  formatValue = formatOcrInteger,
  onChange
}: SettingsRangeFieldProps) {
  const normalizedValue = Math.min(max, Math.max(min, value));
  return (
    <label className="field-group settings-range-field" htmlFor={id}>
      <span>{label}</span>
      <span className="settings-range-field__value">
        {formatValue(normalizedValue)}
        {hint && <small>{hint}</small>}
      </span>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={normalizedValue}
        disabled={disabled}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  );
}

export function AdjustableModelContextBar({
  title,
  sliderLabel,
  loading,
  details,
  fallbackText,
  value,
  recommendedValue,
  onAutoChange,
  onValueChange
}: {
  title: string;
  sliderLabel: string;
  loading: boolean;
  details: OllamaModelDetails | null;
  fallbackText: string;
  value: number | null;
  recommendedValue: number | null;
  onAutoChange: (isAutomatic: boolean) => void;
  onValueChange: (value: number) => void;
}) {
  const nativeNumCtx = details?.numCtx ?? null;
  const activeValue = value ?? nativeNumCtx;
  const trackLabel = value == null
    ? nativeNumCtx
      ? `${nativeNumCtx.toLocaleString("it-IT")} token (nativo)`
      : "Automatico"
    : nativeNumCtx
      ? `${value.toLocaleString("it-IT")} / ${nativeNumCtx.toLocaleString("it-IT")} token`
      : `${value.toLocaleString("it-IT")} token`;
  const trackTitle = value == null
    ? nativeNumCtx
      ? `Finestra nativa: ${nativeNumCtx.toLocaleString("it-IT")} token`
      : "Automatico"
    : nativeNumCtx
      ? `${value.toLocaleString("it-IT")} / ${nativeNumCtx.toLocaleString("it-IT")} token`
      : `${value.toLocaleString("it-IT")} token`;

  return (
    <div className="model-context-bar">
      <div className="model-context-bar__label">
        <span>{title}</span>
        {loading && <span className="model-context-bar__hint">Caricamento...</span>}
        {!loading && details?.numCtx && (
          <span className="model-context-bar__hint">
            Finestra nativa: {details.numCtx.toLocaleString("it-IT")} token
          </span>
        )}
        {!loading && !details?.numCtx && (
          <span className="model-context-bar__hint">{fallbackText}</span>
        )}
      </div>
      <label className="toggle-row" htmlFor={`${sliderLabel.replaceAll(" ", "-")}-auto`}>
        <input
          id={`${sliderLabel.replaceAll(" ", "-")}-auto`}
          type="checkbox"
          checked={value == null}
          onChange={(event) => onAutoChange(event.target.checked)}
        />
        <span>Automatico</span>
      </label>
      {value != null && (
        <SettingsRangeField
          id={sliderLabel.replaceAll(" ", "-")}
          label={sliderLabel}
          min={64}
          max={131072}
          step={64}
          value={value}
          formatValue={(currentValue) => `${currentValue.toLocaleString("it-IT")} token`}
          hint={recommendedValue ? `Suggerito: ${recommendedValue}` : null}
          onChange={onValueChange}
        />
      )}
      {activeValue && (
        <div className="model-context-bar__track">
          <div
            className="model-context-bar__fill"
            style={{ width: `${nativeNumCtx && value != null ? Math.min(100, Math.round((value / nativeNumCtx) * 100)) : 100}%` }}
            title={trackTitle}
          />
          <span className="model-context-bar__track-label">
            {trackLabel}
          </span>
        </div>
      )}
    </div>
  );
}

export function OcrRangeField({
  id,
  label,
  tooltip,
  min,
  max,
  step = 1,
  value,
  formatValue = formatOcrInteger,
  onChange
}: OcrRangeFieldProps) {
  return (
    <label className="field-group ocr-range-field" htmlFor={id}>
      <OcrFieldLabel text={label} tooltip={tooltip} />
      <span className="ocr-range-field__value">{formatValue(value)}</span>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        title={tooltip}
        onChange={(event) => onChange(Number(event.target.value))}
      />
      <span className="ocr-range-field__scale" aria-hidden="true">
        <span>Veloce</span>
        <span>Accurato</span>
      </span>
    </label>
  );
}

export function OcrFieldLabel({ text, tooltip }: { text: string; tooltip: string }) {
  return (
    <span className="ocr-field-label">
      <span>{text}</span>
      <span className="ocr-tooltip" title={tooltip} aria-label={tooltip}>?</span>
    </span>
  );
}

export function getOcrSelectOptions(currentValue: string, knownValues: string[]): string[] {
  const current = currentValue.trim();
  const options = new Set(knownValues);
  if (current.length > 0) {
    options.add(current);
  }

  return [...options];
}

export function formatOcrInteger(value: number): string {
  return Math.round(value).toLocaleString("it-IT");
}

export function formatOcrDecimal(value: number): string {
  return value.toFixed(2);
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
  return {
    maxParallelJobs: Number(settings.maxParallelJobs),
    maxOcrParallelPages: Number(settings.maxOcrParallelPages),
    embeddingBatchSize: Number(settings.embeddingBatchSize),
    translationBatchSize: Number(settings.translationBatchSize),
    maxContextChunks: Number(settings.maxContextChunks),
    requestTimeoutSeconds: Number(settings.requestTimeoutSeconds),
    enableLowResourceMode: settings.enableLowResourceMode
  };
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

export function clampNumber(value: number, min: number, max: number): number {
  if (!Number.isFinite(value)) {
    return min;
  }

  return Math.min(max, Math.max(min, value));
}

export function formatModelSize(size: number): string {
  if (size >= 1_000_000_000) {
    return `${(size / 1_000_000_000).toFixed(1)} GB`;
  }

  if (size >= 1_000_000) {
    return `${(size / 1_000_000).toFixed(1)} MB`;
  }

  return `${size} B`;
}

