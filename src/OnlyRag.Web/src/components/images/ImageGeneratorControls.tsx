import type { FormEvent } from "react";
import {
  generationProfiles,
  imageTooltips,
  minSizePresets,
  promptLanguages,
  socialSizePresets,
  standardSizePresets,
  type GenerationProfile,
  type PromptLanguage
} from "./imageTypes";

type ImageGeneratorControlsProps = {
  prompt: string;
  onPromptChange: (value: string) => void;
  promptLanguage: PromptLanguage;
  onPromptLanguageChange: (lang: PromptLanguage) => void;
  negativePrompt: string;
  onNegativePromptChange: (value: string) => void;
  width: number;
  height: number;
  onSizeChange: (width: number, height: number) => void;
  generationProfile: GenerationProfile;
  onGenerationProfileChange: (profile: GenerationProfile) => void;
  steps: number;
  onStepsChange: (steps: number) => void;
  seed: string;
  onSeedChange: (value: string) => void;
  guidanceScale: string;
  onGuidanceScaleChange: (value: string) => void;
  canGenerate: boolean;
  isGenerating: boolean;
  onGenerate: (event: FormEvent) => void;
};

export function ImageGeneratorControls({
  prompt,
  onPromptChange,
  promptLanguage,
  onPromptLanguageChange,
  negativePrompt,
  onNegativePromptChange,
  width,
  height,
  onSizeChange,
  generationProfile,
  onGenerationProfileChange,
  steps,
  onStepsChange,
  seed,
  onSeedChange,
  guidanceScale,
  onGuidanceScaleChange,
  canGenerate,
  isGenerating,
  onGenerate
}: ImageGeneratorControlsProps) {
  return (
    <div className="images-controls-panel">
      <h3>Crea immagine</h3>
      <form onSubmit={onGenerate} className="images-form">
        <div className="settings-grid settings-grid--two">
          <label className="field-group" htmlFor="prompt-language">
            <span>Lingua Prompt</span>
            <select
              id="prompt-language"
              value={promptLanguage}
              onChange={(e) => onPromptLanguageChange(e.target.value as PromptLanguage)}
              title="Seleziona la lingua con cui scrivi il prompt. Se diversa da Inglese, verrà tradotto automaticamente."
            >
              {promptLanguages.map((lang) => (
                <option key={lang.value} value={lang.value}>
                  {lang.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <label className="field-group" htmlFor="image-prompt">
          <span>Prompt {promptLanguage !== "en" ? "(Verrà tradotto in Inglese)" : ""}</span>
          <textarea
            id="image-prompt"
            rows={3}
            value={prompt}
            onChange={(e) => onPromptChange(e.target.value)}
            placeholder={
              promptLanguage === "it"
                ? "Un astronauta a cavallo di un cavallo dorato, stile cyberpunk..."
                : "An astronaut riding a golden horse, cyberpunk style..."
            }
            required
          />
        </label>
        <small className="image-prompt-hint">
          {promptLanguage !== "en"
            ? "🌐 Traduzione automatica attiva: il prompt verrà convertito in inglese prima della generazione."
            : "💡 I modelli SDXL generano i migliori risultati con prompt in inglese."}
        </small>

        <div className="field-group">
          <span>Preset Risoluzione</span>
          
          <div className="resolution-category">
            <span className="resolution-category__title">⚡ Minima / Veloce</span>
            <div className="preset-buttons-row">
              {minSizePresets.map((preset) => {
                const isActive = width === preset.width && height === preset.height;
                return (
                  <button
                    type="button"
                    key={preset.label}
                    className={`button-secondary ${isActive ? "button-secondary--active" : ""}`}
                    aria-pressed={isActive}
                    onClick={() => onSizeChange(preset.width, preset.height)}
                  >
                    {preset.label}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="resolution-category">
            <span className="resolution-category__title">🖼️ Standard SDXL</span>
            <div className="preset-buttons-row">
              {standardSizePresets.map((preset) => {
                const isActive = width === preset.width && height === preset.height;
                return (
                  <button
                    type="button"
                    key={preset.label}
                    className={`button-secondary ${isActive ? "button-secondary--active" : ""}`}
                    aria-pressed={isActive}
                    onClick={() => onSizeChange(preset.width, preset.height)}
                  >
                    {preset.label}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="resolution-category">
            <span className="resolution-category__title">📱 Formati Social Media</span>
            <div className="preset-buttons-row">
              {socialSizePresets.map((preset) => {
                const isActive = width === preset.width && height === preset.height;
                return (
                  <button
                    type="button"
                    key={preset.label}
                    className={`button-secondary ${isActive ? "button-secondary--active" : ""}`}
                    aria-pressed={isActive}
                    onClick={() => onSizeChange(preset.width, preset.height)}
                  >
                    {preset.label}
                  </button>
                );
              })}
            </div>
          </div>

          <div className="settings-grid settings-grid--two custom-dimensions-grid">
            <label className="field-group" htmlFor="custom-width">
              <span>Larghezza (px)</span>
              <input
                id="custom-width"
                type="number"
                min={256}
                max={2048}
                step={64}
                value={width}
                onChange={(e) => onSizeChange(Number(e.target.value), height)}
              />
            </label>
            <label className="field-group" htmlFor="custom-height">
              <span>Altezza (px)</span>
              <input
                id="custom-height"
                type="number"
                min={256}
                max={2048}
                step={64}
                value={height}
                onChange={(e) => onSizeChange(width, Number(e.target.value))}
              />
            </label>
          </div>
        </div>

        <details className="image-advanced-options">
          <summary>⚙️ Impostazioni avanzate</summary>
          <div className="image-advanced-options__content">
            <label className="field-group" htmlFor="image-negative-prompt">
              <span>Prompt negativo</span>
              <input
                id="image-negative-prompt"
                value={negativePrompt}
                onChange={(e) => onNegativePromptChange(e.target.value)}
                placeholder="sfocato, bassa risoluzione, deformato..."
                title={imageTooltips.negativePrompt}
              />
            </label>

            <div className="settings-grid settings-grid--two">
              <label className="field-group" htmlFor="image-profile">
                <span>Profilo</span>
                <select
                  id="image-profile"
                  value={generationProfile}
                  onChange={(e) => onGenerationProfileChange(e.target.value as GenerationProfile)}
                >
                  {generationProfiles.map((p) => (
                    <option key={p.value} value={p.value}>
                      {p.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className="field-group" htmlFor="image-steps">
                <span>Step ({steps})</span>
                <input
                  id="image-steps"
                  type="range"
                  min={1}
                  max={60}
                  value={steps}
                  onChange={(e) => onStepsChange(Number(e.target.value))}
                />
              </label>
            </div>

            <div className="settings-grid settings-grid--two">
              <label className="field-group" htmlFor="image-seed">
                <span>Seed</span>
                <input
                  id="image-seed"
                  inputMode="numeric"
                  value={seed}
                  onChange={(e) => onSeedChange(e.target.value)}
                  placeholder="Casuale"
                />
              </label>

              <label className="field-group" htmlFor="image-guidance">
                <span>Guidance Scale</span>
                <input
                  id="image-guidance"
                  inputMode="decimal"
                  value={guidanceScale}
                  onChange={(e) => onGuidanceScaleChange(e.target.value)}
                  placeholder="Default modello"
                />
              </label>
            </div>
          </div>
        </details>

        <button type="submit" className="button-primary" disabled={!canGenerate || isGenerating}>
          {isGenerating ? "Generazione in corso..." : "Genera"}
        </button>
      </form>
    </div>
  );
}
