import type { FormEvent } from "react";
import type { ImageModelCatalogEntry } from "../../api";
import {
  generationProfiles,
  getCompatiblePresets,
  imageTooltips,
  type GenerationProfile
} from "./imageTypes";
import { InfoTip } from "../common/InfoTip";

type ImageGeneratorControlsProps = {
  selectedModel: ImageModelCatalogEntry | null;
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
  selectedModel,
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
  const compatiblePresets = getCompatiblePresets(selectedModel);

  return (
    <div className="images-controls-panel">
      <h3>Crea immagine</h3>
      <form onSubmit={onGenerate} className="images-form">
        {/* Quality Preset Buttons */}
        <div className="field-group">
          <span>Preset Qualità</span>
          <div className="preset-buttons-row">
            {generationProfiles.map((prof) => {
              const isActive = generationProfile === prof.value;
              return (
                <button
                  type="button"
                  key={prof.value}
                  className={`button-secondary ${isActive ? "button-secondary--active" : ""}`}
                  aria-pressed={isActive}
                  onClick={() => onGenerationProfileChange(prof.value as GenerationProfile)}
                >
                  {prof.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* Prompt Input */}
        <label className="field-group" htmlFor="image-prompt">
          <span>Prompt</span>
          <textarea
            id="image-prompt"
            rows={3}
            value={prompt}
            onChange={(e) => onPromptChange(e.target.value)}
            placeholder="Descrivi l'immagine da creare..."
            required
          />
        </label>
        <div className="field-secondary-note">
          Traduzione inglese automatica
          <InfoTip label="Informazioni sulla traduzione del prompt">Il prompt viene tradotto automaticamente in inglese per migliorare la compatibilità con i modelli di generazione.</InfoTip>
        </div>

        {/* Resolution Preset */}
        <div className="field-group">
          <span>Risoluzione</span>
          <div className="preset-buttons-row">
            {compatiblePresets.map((preset) => {
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

        {/* Advanced Options */}
        <details className="image-advanced-options">
          <summary>⚙️ Impostazioni avanzate ({steps} step · guidance {guidanceScale || "default"})</summary>
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
