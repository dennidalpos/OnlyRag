import type { FormEvent } from "react";
import {
  generationProfiles,
  imageTooltips,
  sizePresets,
  type GenerationProfile
} from "./imageTypes";

type ImageGeneratorControlsProps = {
  prompt: string;
  onPromptChange: (value: string) => void;
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
        <label className="field-group" htmlFor="image-prompt">
          <span>Prompt</span>
          <textarea
            id="image-prompt"
            rows={3}
            value={prompt}
            onChange={(e) => onPromptChange(e.target.value)}
            placeholder="Un astronauta su un cavallo dorato, stile cyberpunk..."
            required
          />
        </label>

        <div className="field-group">
          <span>Dimensioni</span>
          <div className="preset-buttons-row">
            {sizePresets.map((preset) => {
              const isActive = width === preset.width && height === preset.height;
              return (
                <button
                  type="button"
                  key={preset.label}
                  className={`button-secondary ${isActive ? "button-secondary--active" : ""}`}
                  aria-pressed={isActive}
                  onClick={() => onSizeChange(preset.width, preset.height)}
                >
                  {preset.label} ({preset.width}x{preset.height})
                </button>
              );
            })}
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
