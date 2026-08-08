import { useState, type FormEvent, type RefObject } from "react";
import {
  apiRequest,
  type ImageGenerationSettings,
  type ImageModelCatalogEntry,
  type ImageModelCatalogEntryRequest,
  type ImageModelLocalState,
  type ImageModelUrlVerificationResponse
} from "../../api";
import { formatFileSize } from "../documents/DocumentsSection.formatting";
import { InfoTip } from "../common/InfoTip";
import { ProgressBar } from "../common/ProgressBar";
import { createEmptyModelDraft, modelTemplates, type ModelDraft } from "./imageTypes";

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
  onDeleteCatalogModel?: (modelId: string) => Promise<void>;
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
  onDeleteCatalogModel,
  onUpsertCatalogModel
}: Props) {
  const [localSettings, setLocalSettings] = useState<ImageGenerationSettings>(settings);
  const [showAddForm, setShowAddForm] = useState(false);
  const [draft, setDraft] = useState<ModelDraft>(createEmptyModelDraft("custom-sdxl-onnx"));
  const [selectedTemplateId, setSelectedTemplateId] = useState<string>("");
  const [isVerifyingUrl, setIsVerifyingUrl] = useState(false);
  const [verifyFeedback, setVerifyFeedback] = useState<{ tone: "success" | "error" | "warning"; message: string } | null>(null);

  if (!isOpen) return null;

  function handleSelectTemplate(templateId: string) {
    setSelectedTemplateId(templateId);
    setVerifyFeedback(null);
    const tmpl = modelTemplates.find((t) => t.id === templateId);
    if (tmpl) {
      setDraft((prev) => ({
        ...prev,
        id: tmpl.id,
        displayName: tmpl.displayName,
        downloadUrl: tmpl.downloadUrl,
        licenseLabel: tmpl.licenseLabel,
        expectedSizeBytes: tmpl.expectedSizeBytes,
        modelType: tmpl.modelType,
        modelProfile: tmpl.modelProfile,
        supportedResolutions: tmpl.supportedResolutions,
        defaultSteps: tmpl.defaultSteps,
        defaultGuidance: tmpl.defaultGuidance,
        scheduler: tmpl.scheduler,
        compatibilityNotes: tmpl.compatibilityNotes,
        requiredFiles: "model_index.json, scheduler/scheduler_config.json, text_encoder/model.onnx, text_encoder_2/model.onnx, tokenizer/merges.txt, tokenizer/special_tokens_map.json, tokenizer/tokenizer_config.json, tokenizer/vocab.json, tokenizer_2/merges.txt, tokenizer_2/special_tokens_map.json, tokenizer_2/tokenizer_config.json, tokenizer_2/vocab.json, unet/model.onnx, vae_decoder/model.onnx, vae_encoder/model.onnx"
      }));
    }
  }

  async function handleVerifyUrl() {
    if (!draft.downloadUrl.trim()) {
      setVerifyFeedback({ tone: "warning", message: "Inserisci prima un URL di download modello da verificare." });
      return;
    }

    setIsVerifyingUrl(true);
    setVerifyFeedback(null);
    try {
      const res = await apiRequest<ImageModelUrlVerificationResponse>("/api/images/models/verify-url", {
        method: "POST",
        body: JSON.stringify({ url: draft.downloadUrl.trim() })
      });

      if (res.isValid) {
        setVerifyFeedback({ tone: "success", message: res.message });
        setDraft((prev) => ({
          ...prev,
          displayName: prev.displayName.trim() || res.suggestedDisplayName,
          expectedSizeBytes: res.totalSizeBytes > 0 ? res.totalSizeBytes.toString() : prev.expectedSizeBytes,
          requiredFiles: res.suggestedRequiredFiles.length > 0 ? res.suggestedRequiredFiles.join(", ") : prev.requiredFiles
        }));
      } else {
        setVerifyFeedback({ tone: "error", message: res.message });
      }
    } catch (err) {
      setVerifyFeedback({
        tone: "error",
        message: err instanceof Error ? err.message : "Verifica URL fallita."
      });
    } finally {
      setIsVerifyingUrl(false);
    }
  }

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

            <div className="checkbox-field" style={{ display: "flex", alignItems: "center", gap: "10px", margin: "12px 0" }}>
              <input
                id="settings-prefer-gpu"
                type="checkbox"
                checked={localSettings.preferGpu}
                onChange={(e) => setLocalSettings({ ...localSettings, preferGpu: e.target.checked })}
              />
              <label htmlFor="settings-prefer-gpu" style={{ display: "inline-flex", alignItems: "center", gap: "6px", cursor: "pointer", margin: 0 }}>
                <span>Usa accelerazione GPU</span>
              </label>
              <InfoTip label="Spiegazione accelerazione GPU" placement="bottom">
                Utilizza DirectML per accelerare l'inferenza della generazione immagini sulla scheda grafica Windows.
              </InfoTip>
            </div>

            {selectedModel && (
              <ModelReadiness
                model={selectedModel}
                state={selectedModelState}
                onAskConsent={() => onAskConsent(selectedModel.id)}
                onDelete={() => onDeleteModel(selectedModel.id)}
                onDeleteCatalogModel={onDeleteCatalogModel ? () => onDeleteCatalogModel(selectedModel.id) : undefined}
                disabled={isModelActionRunning}
              />
            )}

            <div className="modal-footer" style={{ marginTop: "16px", display: "flex", gap: "12px", flexWrap: "wrap" }}>
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
                  {showAddForm ? "Chiudi modulo aggiunta" : "➕ Aggiungi altro modello"}
                </button>
              )}
            </div>
          </form>

          {showAddForm && (
            <form className="image-advanced-options__content" onSubmit={handleAddCatalogModelSubmit}>
              <h4>➕ Aggiungi modello al catalogo</h4>

              <label className="field-group">
                <span>Template preconfigurato</span>
                <select
                  value={selectedTemplateId}
                  onChange={(e) => handleSelectTemplate(e.target.value)}
                >
                  <option value="">-- Seleziona un modello o incollalo --</option>
                  {modelTemplates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.displayName} ({t.downloadUrl})
                    </option>
                  ))}
                </select>
              </label>

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

              <div className="field-group">
                <span>URL Download (Repository HF o file ONNX) *</span>
                <div style={{ display: "flex", gap: "8px" }}>
                  <input
                    type="url"
                    required
                    style={{ flex: 1 }}
                    value={draft.downloadUrl}
                    onChange={(e) => setDraft({ ...draft, downloadUrl: e.target.value })}
                    placeholder="https://huggingface.co/owner/model-repo"
                  />
                  <button
                    type="button"
                    className="button-secondary"
                    onClick={handleVerifyUrl}
                    disabled={isVerifyingUrl || !draft.downloadUrl.trim()}
                    title="Verifica accessibilita e file del repository prima di salvare"
                  >
                    {isVerifyingUrl ? "Verifica..." : "🔍 Verifica URL"}
                  </button>
                </div>
              </div>

              {verifyFeedback && (
                <div className={`feedback-alert feedback-alert--${verifyFeedback.tone}`} role="alert">
                  {verifyFeedback.message}
                </div>
              )}

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
                <button type="submit" className="button-primary" disabled={isModelActionRunning || isVerifyingUrl}>
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
  onDeleteCatalogModel,
  disabled
}: {
  model: ImageModelCatalogEntry;
  state: ImageModelLocalState | null;
  onAskConsent: () => void;
  onDelete: () => void;
  onDeleteCatalogModel?: () => void;
  disabled: boolean;
}) {
  const [showDetails, setShowDetails] = useState(false);
  const localSizeBytes = state?.localSizeBytes ?? 0;
  const sizeFormatted = localSizeBytes > 0 ? formatFileSize(localSizeBytes) : null;
  const isVerified = Boolean(state?.isVerified);

  return (
    <div className={isVerified ? "image-status image-status--online" : "image-status image-status--offline"} style={{ marginTop: "12px", padding: "12px", borderRadius: "8px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
        <strong>{model.displayName}</strong>
        <span className={`status-badge ${isVerified ? "status-badge--success" : "status-badge--warning"}`}>
          {isVerified ? "✅ Pronto all'uso" : state?.state === "Downloading" ? "⏳ Download in corso..." : "⬇️ Non Presente Locale"}
        </span>
      </div>

      <small style={{ display: "block", marginBottom: "8px", color: "var(--color-text-muted, #94a3b8)" }}>
        {isVerified
          ? `File verificati (${formatFileSize(state?.localSizeBytes ?? 0)}) · Modello locale pronto`
          : `${state?.verificationError ?? model.recommendedProfile} · ${formatRemainingDownload(state, model)}`}
      </small>

      {state?.state === "Downloading" && (
        <div style={{ marginBottom: "12px", display: "flex", flexDirection: "column", gap: "6px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", fontSize: "12px", fontWeight: 600, color: "var(--text-main)" }}>
            <span>⏳ Download e sincronizzazione file in corso...</span>
            <span>{state.localSizeBytes > 0 && state.expectedSizeBytes > 0 ? `${Math.round((state.localSizeBytes / state.expectedSizeBytes) * 100)}%` : "In corso"}</span>
          </div>
          <ProgressBar
            label="Avanzamento download modello ONNX"
            value={state.localSizeBytes > 0 && state.expectedSizeBytes > 0 ? Math.round((state.localSizeBytes / state.expectedSizeBytes) * 100) : 0}
            indeterminate={state.localSizeBytes <= 0}
          />
        </div>
      )}

      {/* Main Download & Action Buttons */}
      <div className="settings-actions" style={{ display: "flex", gap: "8px", flexWrap: "wrap", marginBottom: "10px" }}>
        {!isVerified && (
          <button
            type="button"
            className="button-primary"
            onClick={onAskConsent}
            disabled={disabled}
            title="Scarica i file necessari del modello ONNX locale sul PC"
          >
            Scarica modello
          </button>
        )}

        <button
          type="button"
          className="button-secondary button-secondary--xs"
          onClick={() => setShowDetails(!showDetails)}
          title="Consulta i metadati, requisiti e parametri del modello"
        >
          ℹ️ {showDetails ? "Nascondi dettagli" : "Dettagli e parametri modello"}
        </button>

        {state?.isDownloaded && (
          <button
            className="button-danger button-danger--xs"
            type="button"
            onClick={onDelete}
            disabled={disabled}
            title={sizeFormatted ? `Rimuovi i file del modello dal disco (${sizeFormatted})` : "Rimuovi file dal disco"}
          >
            🗑️ Rimuovi file modello locali {sizeFormatted ? `(${sizeFormatted})` : ""}
          </button>
        )}

        {onDeleteCatalogModel && !model.isBuiltIn && (
          <button
            className="button-danger button-danger--xs"
            type="button"
            onClick={onDeleteCatalogModel}
            disabled={disabled}
            title="Rimuovi definitivamente questo modello dal catalogo"
          >
            ❌ Rimuovi dal catalogo
          </button>
        )}
      </div>

      {/* Consultable / Configurable Info Metadata Drawer */}
      {showDetails && (
        <dl className="image-model-metadata" style={{ marginTop: "10px", padding: "10px", background: "rgba(0,0,0,0.15)", borderRadius: "6px" }}>
          <div>
            <dt>Tipo Modello</dt>
            <dd>{model.modelType} ({model.modelProfile})</dd>
          </div>
          <div>
            <dt>Risoluzioni Supportate</dt>
            <dd>{model.supportedResolutions.join(", ")}</dd>
          </div>
          <div>
            <dt>Default Consigliati</dt>
            <dd>{model.defaultSteps} step · guidance scale {model.defaultGuidance}</dd>
          </div>
          <div>
            <dt>Scheduler</dt>
            <dd>{model.scheduler}</dd>
          </div>
          <div>
            <dt>Licenza</dt>
            <dd>{model.licenseLabel}</dd>
          </div>
          <div>
            <dt>Compatibilità Windows</dt>
            <dd>{model.compatibilityNotes}</dd>
          </div>
          <div>
            <dt>URL Sorgente Upstream</dt>
            <dd style={{ wordBreak: "break-all" }}>
              <a href={model.downloadUrl} target="_blank" rel="noreferrer" style={{ color: "#38bdf8" }}>
                {model.downloadUrl}
              </a>
            </dd>
          </div>
        </dl>
      )}
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
