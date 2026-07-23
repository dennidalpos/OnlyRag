import type { ImageGenerationRuntimeStatus, ImageModelCatalogEntry, ImageModelLocalState } from "../../api";

type Props = {
  runtimeStatus: ImageGenerationRuntimeStatus | null;
  selectedModel: ImageModelCatalogEntry | null;
  selectedModelState: ImageModelLocalState | null;
  onOpenSettings: () => void;
  isModelActionRunning: boolean;
};

export function ImageHardwareStatusHeader({
  runtimeStatus,
  selectedModel,
  selectedModelState,
  onOpenSettings,
  isModelActionRunning
}: Props) {
  const isDirectMl = runtimeStatus?.executionProvider?.toLowerCase().includes("directml");

  return (
    <div className="image-gen-header">
      <div className="image-gen-header__info">
        <h2>Generazione Immagini ONNX</h2>
        <div className="image-gen-header__badges">
          <span className={`status-badge ${isDirectMl ? "status-badge--online" : "status-badge--warning"}`}>
            DirectML: {isDirectMl ? "GPU DirectML Attiva" : "CPU Fallback"}
          </span>
          {selectedModel && (
            <span className={`status-badge ${selectedModelState?.isVerified ? "status-badge--online" : "status-badge--warning"}`}>
              {selectedModel.displayName} {selectedModelState?.isVerified ? "✓ In Locale" : "⚠ Non Scaricato"}
            </span>
          )}
        </div>
      </div>

      <div className="image-gen-header__actions">
        <a
          href="https://huggingface.co/models?search=onnx+sdxl"
          target="_blank"
          rel="noopener noreferrer"
          className="button-secondary"
          title="Apri la pagina web Hugging Face con i modelli ONNX SDXL/LCM compatibili"
        >
          🌐 Modelli Compatibili (Hugging Face)
        </a>
        <button type="button" className="button-secondary" onClick={onOpenSettings} disabled={isModelActionRunning} title="Apri gestione modelli e impostazioni immagini">
          ⚙️ Impostazioni modelli
        </button>
      </div>
    </div>
  );
}
