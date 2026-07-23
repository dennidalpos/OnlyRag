import { useState, type FormEvent, type RefObject } from "react";
import type {
  ImageGenerationSettings,
  ImageModelCatalogEntry,
  ImageModelCatalogEntryRequest,
  ImageModelLocalState
} from "../../api";
import { formatFileSize } from "../DocumentsSection.formatting";
import { createEmptyModelDraft, type ModelDraft } from "./imageTypes";

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
  onUpsertCatalogModel?: (request: ImageModelCatalogEntryRequest) => Promise<void>;
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
  onDeleteModel,
  onUpsertCatalogModel
}: Props) {
  const [localSettings, setLocalSettings] = useState<ImageGenerationSettings>(settings);
  const [showAddForm, setShowAddForm] = useState(false);
  const [draft, setDraft] = useState<ModelDraft>(createEmptyModelDraft("custom-sdxl-onnx"));

  if (!isOpen) return null;

  async function handleFormSubmit(event: FormEvent) {
    event.preventDefault();
    await onSaveSettings(localSettings);
  }

  async function handleAddCatalogModelSubmit(event: FormEvent) {
    event.preventDefault();
    if (!onUpsertCatalogModel) return;

    const request: ImageModelCatalogEntryRequest = {
      id: draft.id.trim(),
      displayName: draft.displayName.trim() || draft.id.trim(),
      recommendedProfile: draft.recommendedProfile.trim() || "Profilo ONNX personalizzato",
      downloadUrl: draft.downloadUrl.trim(),
      licenseLabel: draft.licenseLabel.trim() || "OpenRAIL-M",
      expectedSizeBytes: Number(draft.expectedSizeBytes) || 0,
      requiredFiles: draft.requiredFiles.split(",").map((f) => f.trim()).filter(Boolean),
      sha256: draft.sha256.trim(),
      modelType: draft.modelType.trim() || "SDXL ONNX",
      modelProfile: draft.modelProfile.trim() || "custom",
      supportedResolutions: draft.supportedResolutions.split(",").map((r) => r.trim()).filter(Boolean),
      defaultSteps: Number(draft.defaultSteps) || 6,
      defaultGuidance: Number(draft.defaultGuidance) || 1.0,
      scheduler: draft.scheduler.trim() || "LCM",
      compatibilityNotes: draft.compatibilityNotes.trim() || "DirectML GPU consigliato, fallback CPU disponibile."
    };

    await onUpsertCatalogModel(request);
    setShowAddForm(false);
  }

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="settings-dialog-title">
      <div className={`modal-content image-settings-modal ${isMaximized ? "modal-content--maximized" : ""}`} ref={modalRef}>
        <div className="modal-header">
          <h3 id="settings-dialog-title">Impostazioni immagini e modelli</h3>
          <div className="modal-header__actions">
            <button
              type="button"
              className="button-icon"
              onClick={onToggleMaximize}
              title={isMaximized ? "Ripristina dimensione finestra" : "Ingrandisci finestra a schermo intero"}
              aria-label={isMaximized ? "Ripristina dimensione" : "Massimizza"}
            >
              {isMaximized ? "🗗" : "🗖"}
            </button>
            <button
              type="button"
              className="button-icon"
              onClick={onClose}
              title="Chiudi finestra impostazioni"
              aria-label="Chiudi finestra"
            >
              ✕
            </button>
          </div>
        </div>
        <div className="modal-body image-settings-modal__body">
          <form onSubmit={handleFormSubmit}>
            <label className="field-group" htmlFor="settings-selected-model">
              <span>Modello selezionato</span>
              <select
                id="settings-selected-model"
                value={localSettings.selectedModelId}
                onChange={(e) => {
                  setLocalSettings({ ...localSettings, selectedModelId: e.target.value });
                }}
              >
                {catalog.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.displayName} ({m.isBuiltIn ? "Integrato" : "Personalizzato"})
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
              <span>Preferisci DirectML GPU (accelera l'inferenza su scheda grafica Windows)</span>
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
              <button type="submit" className="button-primary" disabled={isSaving} title="Salva impostazioni selezionate">
                {isSaving ? "Salvataggio..." : "Salva impostazioni"}
              </button>
              {onUpsertCatalogModel && (
                <button
                  type="button"
                  className="button-secondary"
                  onClick={() => setShowAddForm(!showAddForm)}
                  title="Aggiungi un nuovo modello ONNX personalizzato al catalogo"
                >
                  {showAddForm ? "Annulla nuovo modello" : "➕ Aggiungi altro modello"}
                </button>
              )}
            </div>
          </form>

          {showAddForm && (
            <form className="image-advanced-options__content" onSubmit={handleAddCatalogModelSubmit}>
              <h4>➕ Aggiungi modello al catalogo</h4>
              <div className="settings-grid settings-grid--two">
                <label className="field-group">
                  <span>ID Modello *</span>
                  <input
                    type="text"
                    required
                    value={draft.id}
                    onChange={(e) => setDraft({ ...draft, id: e.target.value })}
                    placeholder="lcm-sdxl-custom"
                  />
                </label>

                <label className="field-group">
                  <span>Nome Visibile *</span>
                  <input
                    type="text"
                    required
                    value={draft.displayName}
                    onChange={(e) => setDraft({ ...draft, displayName: e.target.value })}
                    placeholder="Custom SDXL LCM ONNX"
                  />
                </label>
              </div>

              <label className="field-group">
                <span>URL Download (Repository HF o file ONNX) *</span>
                <input
                  type="url"
                  required
                  value={draft.downloadUrl}
                  onChange={(e) => setDraft({ ...draft, downloadUrl: e.target.value })}
                  placeholder="https://huggingface.co/owner/model-repo"
                />
              </label>

              <div className="settings-grid settings-grid--two">
                <label className="field-group">
                  <span>File Richiesti (separati da virgola)</span>
                  <input
                    type="text"
                    value={draft.requiredFiles}
                    onChange={(e) => setDraft({ ...draft, requiredFiles: e.target.value })}
                    placeholder="model.onnx, text_encoder/model.onnx, unet/model.onnx"
                  />
                </label>

                <label className="field-group">
                  <span>Etichetta Licenza</span>
                  <input
                    type="text"
                    value={draft.licenseLabel}
                    onChange={(e) => setDraft({ ...draft, licenseLabel: e.target.value })}
                    placeholder="OpenRAIL-M"
                  />
                </label>
              </div>

              <div className="modal-footer">
                <button type="submit" className="button-primary" disabled={isModelActionRunning}>
                  Salva nel catalogo
                </button>
              </div>
            </form>
          )}
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
          <button type="button" className="button-primary" onClick={onAskConsent} disabled={disabled} title="Scarica i file del modello dal repository configurato">
            Scarica modello
          </button>
        )}
        {state?.isDownloaded && (
          <button className="button-danger" type="button" onClick={onDelete} disabled={disabled} title="Rimuovi i file scaricati del modello dal disco">
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
