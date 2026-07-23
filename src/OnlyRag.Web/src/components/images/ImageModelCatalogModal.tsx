import { useState, type FormEvent, type RefObject } from "react";
import type {
  ImageGenerationSettings,
  ImageModelCatalogEntry,
  ImageModelLocalState
} from "../../api";
import { formatFileSize } from "../DocumentsSection.formatting";

type Props = {
  isOpen: boolean;
  isMaximized: boolean;
  modalRef: RefObject<HTMLDivElement | null>;
  settings: ImageGenerationSettings;
  catalog: ImageModelCatalogEntry[];
  modelStates: ImageModelLocalState[];
  selectedModel: ImageModelCatalogEntry | null;
  selectedModelState: ImageModelLocalState | null;
  isSaving: boolean;
  isModelActionRunning: boolean;
  onClose: () => void;
  onToggleMaximize: () => void;
  onSaveSettings: (settings: ImageGenerationSettings) => Promise<void>;
  onAskConsent: (modelId: string) => void;
  onDeleteModel: (modelId: string) => Promise<void>;
};

export function ImageModelCatalogModal({
  isOpen,
  isMaximized,
  modalRef,
  settings,
  catalog,
  selectedModel,
  selectedModelState,
  isSaving,
  isModelActionRunning,
  onClose,
  onToggleMaximize,
  onSaveSettings,
  onAskConsent,
  onDeleteModel
}: Props) {
  const [localSettings, setLocalSettings] = useState<ImageGenerationSettings>(settings);

  if (!isOpen) return null;

  async function handleFormSubmit(event: FormEvent) {
    event.preventDefault();
    await onSaveSettings(localSettings);
  }

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="settings-dialog-title">
      <div className={`modal-content ${isMaximized ? "modal-content--maximized" : ""}`} ref={modalRef}>
        <div className="modal-header">
          <h3 id="settings-dialog-title">Impostazioni immagini</h3>
          <div className="modal-header__actions">
            <button type="button" className="button-icon" onClick={onToggleMaximize}>
              {isMaximized ? "🗗" : "🗖"}
            </button>
            <button type="button" className="button-icon" onClick={onClose}>
              ✕
            </button>
          </div>
        </div>
        <div className="modal-body">
          <form onSubmit={handleFormSubmit}>
            <label className="field-group" htmlFor="settings-selected-model">
              <span>Modello integrato</span>
              <select
                id="settings-selected-model"
                value={localSettings.selectedModelId}
                onChange={(e) => {
                  setLocalSettings({ ...localSettings, selectedModelId: e.target.value });
                }}
              >
                {catalog.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.displayName}
                  </option>
                ))}
              </select>
            </label>

            <label className="field-group checkbox-group" htmlFor="settings-prefer-gpu">
              <input
                id="settings-prefer-gpu"
                type="checkbox"
                checked={localSettings.preferGpu}
                onChange={(e) => setLocalSettings({ ...localSettings, preferGpu: e.target.checked })}
              />
              <span>Preferisci DirectML GPU</span>
            </label>

            {selectedModel && (
              <ModelReadiness
                model={selectedModel}
                state={selectedModelState}
                onAskConsent={() => onAskConsent(selectedModel.id)}
                onDelete={() => onDeleteModel(selectedModel.id)}
                disabled={isModelActionRunning}
              />
            )}

            <div className="modal-footer">
              <button type="submit" className="button-primary" disabled={isSaving}>
                {isSaving ? "Salvataggio..." : "Salva"}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}

function ModelReadiness({
  model,
  state,
  onAskConsent,
  onDelete,
  disabled
}: {
  model: ImageModelCatalogEntry;
  state: ImageModelLocalState | null;
  onAskConsent: () => void;
  onDelete: () => void;
  disabled: boolean;
}) {
  return (
    <div className={state?.isVerified ? "image-status image-status--online" : "image-status image-status--offline"}>
      <strong>{model.displayName}</strong>
      <span>{state?.state ?? "NotDownloaded"}</span>
      <small>
        {state?.isVerified
          ? `${formatFileSize(state.localSizeBytes)} pronti`
          : `${state?.verificationError ?? model.recommendedProfile} · ${formatRemainingDownload(state, model)}`}
      </small>
      <dl className="image-model-metadata">
        <div>
          <dt>Tipo</dt>
          <dd>{model.modelType}</dd>
        </div>
        <div>
          <dt>Risoluzioni</dt>
          <dd>{model.supportedResolutions.join(", ")}</dd>
        </div>
        <div>
          <dt>Default</dt>
          <dd>{model.defaultSteps} step · guidance {model.defaultGuidance}</dd>
        </div>
        <div>
          <dt>Scheduler</dt>
          <dd>{model.scheduler}</dd>
        </div>
        <div>
          <dt>CPU/GPU</dt>
          <dd>{model.compatibilityNotes}</dd>
        </div>
      </dl>
      <div className="settings-actions">
        {!state?.isVerified && (
          <button type="button" onClick={onAskConsent} disabled={disabled}>
            Scarica modello
          </button>
        )}
        {state?.isDownloaded && (
          <button className="button-secondary" type="button" onClick={onDelete} disabled={disabled}>
            Elimina modello
          </button>
        )}
      </div>
    </div>
  );
}

function formatRemainingDownload(state: ImageModelLocalState | null, model: ImageModelCatalogEntry): string {
  const expected = state?.expectedSizeBytes ?? model.expectedSizeBytes;
  const remaining = state?.remainingDownloadBytes ?? expected;
  if (expected <= 0) {
    return "Dimensione modello non dichiarata";
  }

  return `${formatFileSize(remaining)} rimanenti`;
}
