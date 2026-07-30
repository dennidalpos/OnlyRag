import { useId, useState } from "react";
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

type OcrSelectOption = {
  value: string;
  label: string;
  disabled?: boolean;
};

type OcrSelectFieldProps = {
  id: string;
  label: string;
  tooltip: string;
  value: string;
  options: OcrSelectOption[];
  onChange: (value: string) => void;
};

type SettingsRangeFieldProps = {
  id: string;
  label: string;
  tooltip?: string;
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
  tooltip,
  min,
  max,
  step = 1,
  value,
  disabled = false,
  hint = null,
  formatValue = formatOcrInteger,
  onChange
}: SettingsRangeFieldProps) {
  const descriptionId = useId();
  const normalizedValue = Math.min(max, Math.max(min, value));
  return (
    <label className="field-group settings-range-field" htmlFor={id}>
      <SettingsFieldLabel text={label} tooltip={tooltip} />
      <span className="settings-range-field__value">
        {formatValue(normalizedValue)}
        {hint && <small>{hint}</small>}
      </span>
      {tooltip && <span className="sr-only" id={descriptionId}>{tooltip}</span>}
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={normalizedValue}
        disabled={disabled}
        title={tooltip}
        aria-label={label}
        aria-describedby={tooltip ? descriptionId : undefined}
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
      ? `${nativeNumCtx.toLocaleString("it-IT")} token`
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
            Nativo: {details.numCtx.toLocaleString("it-IT")}
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
      <div className="panel-note panel-note--compact">
        <p>
          {value == null
            ? "Automatico: OnlyRag non invia num_ctx e lascia scegliere a Ollama la finestra effettiva."
            : "Manuale avanzato: OnlyRag invia num_ctx a Ollama. Valori alti possono aumentare RAM/VRAM, ridurre offload GPU e sono da verificare con ollama ps."}
        </p>
      </div>
      {value != null && (
        <SettingsRangeField
          id={sliderLabel.replaceAll(" ", "-")}
          label={sliderLabel}
          tooltip="Imposta manualmente la finestra di contesto inviata a Ollama per questo modello."
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
          <div className="model-context-bar__rail">
            <div
              className="model-context-bar__fill"
              style={{ width: `${nativeNumCtx && value != null ? Math.min(100, Math.round((value / nativeNumCtx) * 100)) : 100}%` }}
              title={trackTitle}
            />
          </div>
          <span className="model-context-bar__track-label">
            {trackLabel}
          </span>
        </div>
      )}
    </div>
  );
}

export function OcrSelectField({
  id,
  label,
  tooltip,
  value,
  options,
  onChange
}: OcrSelectFieldProps) {
  const descriptionId = useId();

  return (
    <label className="field-group" htmlFor={id}>
      <OcrFieldLabel text={label} tooltip={tooltip} />
      <span className="sr-only" id={descriptionId}>{tooltip}</span>
      <select
        id={id}
        value={value}
        title={tooltip}
        aria-label={label}
        aria-describedby={descriptionId}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => (
          <option key={option.value} value={option.value} disabled={option.disabled}>
            {option.label}
          </option>
        ))}
      </select>
    </label>
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
  const descriptionId = useId();

  return (
    <label className="field-group ocr-range-field" htmlFor={id}>
      <OcrFieldLabel text={label} tooltip={tooltip} />
      <span className="ocr-range-field__value">{formatValue(value)}</span>
      <span className="sr-only" id={descriptionId}>{tooltip}</span>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        title={tooltip}
        aria-label={label}
        aria-describedby={descriptionId}
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
  return <SettingsFieldLabel text={text} tooltip={tooltip} />;
}

export function SettingsFieldLabel({ text, tooltip }: { text: string; tooltip?: string }) {
  return (
    <span className="ocr-field-label">
      <span>{text}</span>
      {tooltip && <span className="ocr-tooltip" title={tooltip} aria-hidden="true">?</span>}
    </span>
  );
}

export type UnifiedPresetLevel = "basso" | "medio" | "alto" | "auto" | "disattivato" | "custom";

export function UnifiedPresetBar({
  title,
  subtitle,
  activePreset,
  onSelectPreset,
  allowedPresets = ["basso", "medio", "alto", "custom"],
  disabledOptions = []
}: {
  title: string;
  subtitle?: string;
  activePreset: UnifiedPresetLevel;
  onSelectPreset: (preset: UnifiedPresetLevel) => void;
  allowedPresets?: UnifiedPresetLevel[];
  disabledOptions?: UnifiedPresetLevel[];
}) {
  const allOptions: { id: UnifiedPresetLevel; label: string; icon: string }[] = [
    { id: "basso", label: "Basso", icon: "🌱" },
    { id: "medio", label: "Medio", icon: "⚖️" },
    { id: "alto", label: "Alto", icon: "🚀" },
    { id: "custom", label: "Personalizzato", icon: "🎨" },
    { id: "auto", label: "Auto", icon: "⚡" },
    { id: "disattivato", label: "Disattivato", icon: "⏸️" }
  ];

  const options = allOptions.filter((opt) => allowedPresets.includes(opt.id));

  return (
    <div className="unified-preset-bar">
      <div className="unified-preset-bar__header">
        <span className="unified-preset-bar__title">{title}</span>
        {subtitle && <span className="unified-preset-bar__subtitle">{subtitle}</span>}
      </div>
      <div className="unified-preset-bar__buttons" role="group" aria-label={title}>
        {options.map((opt) => {
          const isDisabled = disabledOptions.includes(opt.id);
          const isActive = activePreset === opt.id;
          return (
            <button
              key={opt.id}
              type="button"
              className={`unified-preset-btn ${isActive ? "unified-preset-btn--active" : ""}`}
              disabled={isDisabled}
              onClick={() => onSelectPreset(opt.id)}
            >
              <span>{opt.icon}</span>
              <span>{opt.label}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

export function ThemeSelectorPanel() {
  const currentTheme = (localStorage.getItem("onlyrag_theme") as "dark" | "light" | "cyber") || "dark";
  const [activeTheme, setActiveTheme] = useState<"dark" | "light" | "cyber">(currentTheme);

  function handleSelectTheme(newTheme: "dark" | "light" | "cyber") {
    setActiveTheme(newTheme);
    document.documentElement.setAttribute("data-theme", newTheme);
    localStorage.setItem("onlyrag_theme", newTheme);
  }

  return (
    <div className="unified-preset-bar">
      <div className="unified-preset-bar__header">
        <span className="unified-preset-bar__title">Tema Visivo dell'Interfaccia</span>
        <span className="unified-preset-bar__subtitle">Personalizza lo stile visivo e i colori dell'applicazione</span>
      </div>
      <div className="unified-preset-bar__buttons" role="group" aria-label="Tema dell'Interfaccia">
        <button
          type="button"
          className={`unified-preset-btn ${activeTheme === "dark" ? "unified-preset-btn--active" : ""}`}
          onClick={() => handleSelectTheme("dark")}
        >
          <span>🌙</span>
          <span>Scuro Midnight</span>
        </button>
        <button
          type="button"
          className={`unified-preset-btn ${activeTheme === "light" ? "unified-preset-btn--active" : ""}`}
          onClick={() => handleSelectTheme("light")}
        >
          <span>☀️</span>
          <span>Chiaro Crisp</span>
        </button>
        <button
          type="button"
          className={`unified-preset-btn ${activeTheme === "cyber" ? "unified-preset-btn--active" : ""}`}
          onClick={() => handleSelectTheme("cyber")}
        >
          <span>⚡</span>
          <span>Cyber Neon</span>
        </button>
      </div>
    </div>
  );
}
