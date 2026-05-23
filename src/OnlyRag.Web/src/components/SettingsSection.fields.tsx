import type { OllamaModelDetails } from "../api";
import { formatOcrInteger } from "./SettingsSection.formatting";

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
      <span className="model-context-bar__hint">
        In Automatico OnlyRag non invia num_ctx e lascia il valore nativo del modello.
      </span>
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
