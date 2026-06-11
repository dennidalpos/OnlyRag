import { useEffect, useMemo, useState, type FormEvent } from "react";
import {
  apiRequest,
  resolveBackendBaseUrl,
  resolveBackendSessionToken,
  type GeneratedImage,
  type ImageModelCatalogEntryRequest,
  type ImageGenerationResponse,
  type ImageGenerationRuntimeStatus,
  type ImageGenerationSettings,
  type ImageModelCatalogEntry,
  type ImageModelDownloadResponse,
  type ImageModelLocalState
} from "../api";
import { formatFileSize } from "./DocumentsSection.formatting";
import { ProgressBar } from "./ProgressBar";

const defaultModelId = "onlyrag-sdxl-turbo-directml";

const defaultSettings: ImageGenerationSettings = {
  selectedModelId: defaultModelId,
  requestTimeoutSeconds: 300,
  preferGpu: true,
  activeExecutionProvider: "CPU"
};

type Feedback = {
  tone: "success" | "error" | "warning";
  message: string;
};

type ModelDraft = {
  id: string;
  displayName: string;
  recommendedProfile: string;
  downloadUrl: string;
  licenseLabel: string;
  expectedSizeBytes: string;
  requiredFiles: string;
  sha256: string;
};

export function ImagesSection() {
  const [settings, setSettings] = useState<ImageGenerationSettings>(defaultSettings);
  const [savedSettings, setSavedSettings] = useState<ImageGenerationSettings>(defaultSettings);
  const [runtimeStatus, setRuntimeStatus] = useState<ImageGenerationRuntimeStatus | null>(null);
  const [catalog, setCatalog] = useState<ImageModelCatalogEntry[]>([]);
  const [modelStates, setModelStates] = useState<ImageModelLocalState[]>([]);
  const [pendingConsentModelId, setPendingConsentModelId] = useState<string | null>(null);
  const [images, setImages] = useState<GeneratedImage[]>([]);
  const [prompt, setPrompt] = useState("");
  const [negativePrompt, setNegativePrompt] = useState("");
  const [width, setWidth] = useState(1024);
  const [height, setHeight] = useState(1024);
  const [steps, setSteps] = useState(30);
  const [batchSize, setBatchSize] = useState(1);
  const [seed, setSeed] = useState("");
  const [modelDraft, setModelDraft] = useState<ModelDraft>(() => createEmptyModelDraft(defaultModelId));
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isSavingModel, setIsSavingModel] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isModelActionRunning, setIsModelActionRunning] = useState(false);
  const [modelActionMessage, setModelActionMessage] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const selectedModel = useMemo(
    () => catalog.find((model) => model.id === settings.selectedModelId) ?? null,
    [catalog, settings.selectedModelId]
  );
  const selectedModelState = useMemo(
    () => modelStates.find((state) => state.modelId === settings.selectedModelId) ?? null,
    [modelStates, settings.selectedModelId]
  );
  const consentModel = useMemo(
    () => catalog.find((model) => model.id === pendingConsentModelId) ?? null,
    [catalog, pendingConsentModelId]
  );
  const hasDirtySettings = JSON.stringify(settings) !== JSON.stringify(savedSettings);
  const canGenerate = Boolean(prompt.trim()) && Boolean(selectedModelState?.isVerified);

  useEffect(() => {
    if (selectedModel) {
      setModelDraft(createModelDraft(selectedModel));
    }
  }, [selectedModel]);

  useEffect(() => {
    let isCancelled = false;

    async function load() {
      setIsLoading(true);
      try {
        const [loadedSettings, loadedRuntimeStatus, loadedCatalog, loadedModelStates, generatedImages] =
          await Promise.all([
            apiRequest<ImageGenerationSettings>("/api/settings/image-generation"),
            apiRequest<ImageGenerationRuntimeStatus>("/api/images/runtime/status"),
            apiRequest<ImageModelCatalogEntry[]>("/api/images/models/catalog"),
            apiRequest<ImageModelLocalState[]>("/api/images/models"),
            apiRequest<GeneratedImage[]>("/api/images")
          ]);
        if (isCancelled) return;
        setSettings(loadedSettings);
        setSavedSettings(loadedSettings);
        setRuntimeStatus(loadedRuntimeStatus);
        setCatalog(loadedCatalog);
        setModelStates(loadedModelStates);
        setImages(generatedImages);
        setFeedback(null);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Immagini non disponibili." });
        }
      } finally {
        if (!isCancelled) {
          setIsLoading(false);
        }
      }
    }

    void load();
    return () => {
      isCancelled = true;
    };
  }, []);

  async function refreshImageState() {
    const [loadedRuntimeStatus, loadedModelStates] = await Promise.all([
      apiRequest<ImageGenerationRuntimeStatus>("/api/images/runtime/status"),
      apiRequest<ImageModelLocalState[]>("/api/images/models")
    ]);
    setRuntimeStatus(loadedRuntimeStatus);
    setModelStates(loadedModelStates);
  }

  async function refreshImageCatalog() {
    const [loadedCatalog, loadedModelStates, loadedRuntimeStatus] = await Promise.all([
      apiRequest<ImageModelCatalogEntry[]>("/api/images/models/catalog"),
      apiRequest<ImageModelLocalState[]>("/api/images/models"),
      apiRequest<ImageGenerationRuntimeStatus>("/api/images/runtime/status")
    ]);
    setCatalog(loadedCatalog);
    setModelStates(loadedModelStates);
    setRuntimeStatus(loadedRuntimeStatus);
  }

  async function handleSaveSettings() {
    setIsSaving(true);
    setFeedback(null);
    try {
      const saved = await apiRequest<ImageGenerationSettings>("/api/settings/image-generation", {
        method: "PUT",
        body: JSON.stringify(settings)
      });
      setSettings(saved);
      setSavedSettings(saved);
      await refreshImageState();
      setFeedback({ tone: "success", message: "Impostazioni immagini salvate." });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Salvataggio non riuscito." });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleDownloadConfirmed(modelId: string) {
    setIsModelActionRunning(true);
    setModelActionMessage("Download modello in corso...");
    setFeedback(null);
    try {
      const response = await apiRequest<ImageModelDownloadResponse>(`/api/images/models/${modelId}/download`, {
        method: "POST",
        body: JSON.stringify({ consentConfirmed: true })
      });
      setPendingConsentModelId(null);
      await refreshImageState();
      setFeedback({ tone: "success", message: response.message });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Download modello non riuscito." });
    } finally {
      setModelActionMessage(null);
      setIsModelActionRunning(false);
    }
  }

  async function handleDeleteModel(modelId: string) {
    setIsModelActionRunning(true);
    setModelActionMessage("Rimozione file modello in corso...");
    setFeedback(null);
    try {
      const response = await apiRequest<ImageModelDownloadResponse>(`/api/images/models/${modelId}`, { method: "DELETE" });
      await refreshImageState();
      setFeedback({ tone: "success", message: response.message });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Rimozione modello non riuscita." });
    } finally {
      setModelActionMessage(null);
      setIsModelActionRunning(false);
    }
  }

  async function handleSaveModel() {
    setIsSavingModel(true);
    setFeedback(null);
    try {
      const request = createModelRequest(modelDraft);
      const saved = await apiRequest<ImageModelCatalogEntry>(`/api/images/models/catalog/${encodeURIComponent(request.id)}`, {
        method: "PUT",
        body: JSON.stringify(request)
      });
      setSettings((current) => ({ ...current, selectedModelId: saved.id }));
      await refreshImageCatalog();
      setFeedback({ tone: "success", message: saved.isBuiltIn ? "Modello integrato aggiornato." : "Modello manuale salvato." });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Salvataggio modello non riuscito." });
    } finally {
      setIsSavingModel(false);
    }
  }

  async function handleResetOrRemoveModel() {
    if (!selectedModel) return;
    setIsSavingModel(true);
    setFeedback(null);
    try {
      await apiRequest<ImageModelCatalogEntry>(`/api/images/models/catalog/${encodeURIComponent(selectedModel.id)}`, {
        method: "DELETE"
      });
      const nextModelId = selectedModel.isBuiltIn ? selectedModel.id : defaultModelId;
      setSettings((current) => ({ ...current, selectedModelId: nextModelId }));
      await refreshImageCatalog();
      setFeedback({ tone: "success", message: selectedModel.isBuiltIn ? "Modello integrato ripristinato." : "Modello manuale rimosso." });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Aggiornamento catalogo non riuscito." });
    } finally {
      setIsSavingModel(false);
    }
  }

  async function handleGenerate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!prompt.trim()) {
      setFeedback({ tone: "error", message: "Inserisci un prompt per generare immagini." });
      return;
    }
    if (!selectedModelState?.isVerified) {
      setFeedback({ tone: "error", message: "Scarica e verifica il modello selezionato prima di generare." });
      return;
    }

    setIsGenerating(true);
    setFeedback(null);
    try {
      const response = await apiRequest<ImageGenerationResponse>("/api/images/generate", {
        method: "POST",
        body: JSON.stringify({
          prompt,
          negativePrompt: negativePrompt.trim() || null,
          modelId: settings.selectedModelId,
          width,
          height,
          steps,
          batchSize,
          seed: seed.trim() ? Number(seed) : null
        })
      });
      setImages((current) => [...response.images, ...current]);
      setFeedback({ tone: "success", message: response.message });
      await refreshImageState();
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Generazione non riuscita." });
    } finally {
      setIsGenerating(false);
    }
  }

  return (
    <div className="images-panel">
      <div className="images-layout">
        <section className="settings-card images-control-panel" aria-labelledby="images-title">
          <div className="settings-card__header">
            <div>
              <h2 id="images-title">Generazione immagini</h2>
              <p>Provider integrato locale con modelli scaricati nella cartella dati dell'app.</p>
            </div>
            <button className="button-secondary" type="button" onClick={() => void refreshImageState()} disabled={isLoading}>
              Aggiorna
            </button>
          </div>

          {feedback && (
            <div className={`feedback-banner feedback-banner--${feedback.tone}`} role={feedback.tone === "error" ? "alert" : "status"}>
              {feedback.message}
            </div>
          )}

          <div className={runtimeStatus?.isReady ? "image-status image-status--online" : "image-status image-status--offline"}>
            <strong>{runtimeStatus?.state ?? "Caricamento"}</strong>
            <span>{runtimeStatus?.executionProvider ?? settings.activeExecutionProvider}</span>
            <small>{runtimeStatus?.message ?? "Lettura stato immagini..."}</small>
          </div>

          <div className="settings-grid settings-grid--two">
            <label className="field-group" htmlFor="image-model">
              <span>Modello integrato</span>
              <select
                id="image-model"
                value={settings.selectedModelId}
                onChange={(event) => setSettings((current) => ({ ...current, selectedModelId: event.target.value }))}
              >
                {catalog.map((model) => (
                  <option value={model.id} key={model.id}>
                    {model.displayName}
                  </option>
                ))}
              </select>
            </label>
            <label className="field-group" htmlFor="image-timeout">
              <span>Timeout</span>
              <input
                id="image-timeout"
                min={10}
                max={1800}
                type="number"
                value={settings.requestTimeoutSeconds}
                onChange={(event) => setSettings((current) => ({ ...current, requestTimeoutSeconds: Number(event.target.value) }))}
              />
            </label>
            <label className="toggle-row images-trust-row" htmlFor="image-prefer-gpu">
              <input
                id="image-prefer-gpu"
                type="checkbox"
                checked={settings.preferGpu}
                onChange={(event) => setSettings((current) => ({ ...current, preferGpu: event.target.checked }))}
              />
              <span>Preferisci GPU DirectML quando disponibile</span>
            </label>
          </div>

          <div className="settings-actions">
            <button type="button" onClick={() => void handleSaveSettings()} disabled={isSaving || !hasDirtySettings}>
              {isSaving ? "Salvataggio..." : "Salva impostazioni"}
            </button>
          </div>

          {selectedModel && (
            <>
              <ModelReadiness
                model={selectedModel}
                state={selectedModelState}
                onAskConsent={() => setPendingConsentModelId(selectedModel.id)}
                onDelete={() => void handleDeleteModel(selectedModel.id)}
                disabled={isModelActionRunning}
              />
              {modelActionMessage && (
                <div className="image-model-progress" role="status">
                  <div className="image-model-progress__header">
                    <strong>{modelActionMessage}</strong>
                    <span>{selectedModel.displayName}</span>
                  </div>
                  <ProgressBar label={modelActionMessage} value={0} indeterminate />
                </div>
              )}
              <ModelCatalogEditor
                draft={modelDraft}
                selectedModel={selectedModel}
                disabled={isSavingModel}
                onChange={setModelDraft}
                onSave={() => void handleSaveModel()}
                onNew={() => setModelDraft(createEmptyModelDraft(`custom-${Date.now()}`))}
                onResetOrRemove={() => void handleResetOrRemoveModel()}
              />
            </>
          )}

          {consentModel && (
            <div className="panel-note panel-note--warning" role="dialog" aria-labelledby="image-model-consent-title">
              <h3 id="image-model-consent-title">Conferma download modello</h3>
              <p>{consentModel.displayName}</p>
              <p>Licenza: {consentModel.licenseLabel}</p>
              <p>Dimensione prevista: {formatFileSize(consentModel.expectedSizeBytes)}</p>
              <p>Destinazione: {selectedModelState?.localDirectory ?? "%LOCALAPPDATA%\\OnlyRag\\models\\images"}</p>
              <div className="settings-actions">
                <button type="button" onClick={() => void handleDownloadConfirmed(consentModel.id)} disabled={isModelActionRunning}>
                  Conferma e scarica
                </button>
                <button className="button-secondary" type="button" onClick={() => setPendingConsentModelId(null)} disabled={isModelActionRunning}>
                  Annulla
                </button>
              </div>
            </div>
          )}

          <form className="images-generate-form" onSubmit={handleGenerate}>
            <label className="field-group" htmlFor="image-prompt">
              <span>Prompt</span>
              <textarea id="image-prompt" rows={4} value={prompt} onChange={(event) => setPrompt(event.target.value)} />
            </label>
            <label className="field-group" htmlFor="image-negative-prompt">
              <span>Negative prompt</span>
              <textarea
                id="image-negative-prompt"
                rows={2}
                value={negativePrompt}
                onChange={(event) => setNegativePrompt(event.target.value)}
              />
            </label>
            <div className="settings-grid settings-grid--four">
              <label className="field-group" htmlFor="image-width">
                <span>Larghezza</span>
                <input id="image-width" min={256} max={2048} step={8} type="number" value={width} onChange={(event) => setWidth(Number(event.target.value))} />
              </label>
              <label className="field-group" htmlFor="image-height">
                <span>Altezza</span>
                <input id="image-height" min={256} max={2048} step={8} type="number" value={height} onChange={(event) => setHeight(Number(event.target.value))} />
              </label>
              <label className="field-group" htmlFor="image-steps">
                <span>Step</span>
                <input id="image-steps" min={1} max={150} type="number" value={steps} onChange={(event) => setSteps(Number(event.target.value))} />
              </label>
              <label className="field-group" htmlFor="image-batch">
                <span>Batch</span>
                <input id="image-batch" min={1} max={4} type="number" value={batchSize} onChange={(event) => setBatchSize(Number(event.target.value))} />
              </label>
              <label className="field-group" htmlFor="image-seed">
                <span>Seed</span>
                <input id="image-seed" inputMode="numeric" value={seed} placeholder="Automatico" onChange={(event) => setSeed(event.target.value)} />
              </label>
            </div>
            {!selectedModelState?.isVerified && (
              <div className="panel-note panel-note--warning" role="status">
                <p>Scarica e verifica il modello selezionato prima di generare.</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="submit" disabled={isGenerating || !canGenerate}>
                {isGenerating ? "Generazione..." : "Genera"}
              </button>
            </div>
          </form>
        </section>

        <section className="settings-card images-gallery-panel" aria-labelledby="images-gallery-title">
          <div className="settings-card__header">
            <h2 id="images-gallery-title">Gallery</h2>
            <span>{images.length}</span>
          </div>
          {images.length === 0 ? (
            <div className="empty-state" role="status">
              <p>Nessuna immagine generata.</p>
            </div>
          ) : (
            <div className="images-gallery">
              {images.map((image) => (
                <GeneratedImageCard image={image} key={image.id} />
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}

function ModelCatalogEditor({
  draft,
  selectedModel,
  disabled,
  onChange,
  onSave,
  onNew,
  onResetOrRemove
}: {
  draft: ModelDraft;
  selectedModel: ImageModelCatalogEntry;
  disabled: boolean;
  onChange: (draft: ModelDraft) => void;
  onSave: () => void;
  onNew: () => void;
  onResetOrRemove: () => void;
}) {
  return (
    <div className="panel-note panel-note--info">
      <h3>Catalogo modello</h3>
      <div className="settings-grid settings-grid--two">
        <label className="field-group" htmlFor="image-model-id">
          <span>Id</span>
          <input id="image-model-id" value={draft.id} onChange={(event) => onChange({ ...draft, id: event.target.value })} />
        </label>
        <label className="field-group" htmlFor="image-model-name">
          <span>Nome</span>
          <input id="image-model-name" value={draft.displayName} onChange={(event) => onChange({ ...draft, displayName: event.target.value })} />
        </label>
      </div>
      <label className="field-group" htmlFor="image-model-url">
        <span>URL download o repository</span>
        <input id="image-model-url" value={draft.downloadUrl} onChange={(event) => onChange({ ...draft, downloadUrl: event.target.value })} />
      </label>
      <label className="field-group" htmlFor="image-model-profile">
        <span>Profilo</span>
        <input id="image-model-profile" value={draft.recommendedProfile} onChange={(event) => onChange({ ...draft, recommendedProfile: event.target.value })} />
      </label>
      <div className="settings-grid settings-grid--two">
        <label className="field-group" htmlFor="image-model-license">
          <span>Licenza</span>
          <input id="image-model-license" value={draft.licenseLabel} onChange={(event) => onChange({ ...draft, licenseLabel: event.target.value })} />
        </label>
        <label className="field-group" htmlFor="image-model-size">
          <span>Dimensione prevista</span>
          <input
            id="image-model-size"
            inputMode="numeric"
            value={draft.expectedSizeBytes}
            onChange={(event) => onChange({ ...draft, expectedSizeBytes: event.target.value })}
          />
        </label>
      </div>
      <label className="field-group" htmlFor="image-model-required-files">
        <span>File richiesti</span>
        <input
          id="image-model-required-files"
          value={draft.requiredFiles}
          onChange={(event) => onChange({ ...draft, requiredFiles: event.target.value })}
        />
      </label>
      <label className="field-group" htmlFor="image-model-sha">
        <span>SHA256 opzionale</span>
        <input id="image-model-sha" value={draft.sha256} onChange={(event) => onChange({ ...draft, sha256: event.target.value })} />
      </label>
      <div className="settings-actions">
        <button type="button" onClick={onSave} disabled={disabled}>
          {disabled ? "Salvataggio..." : "Salva modello"}
        </button>
        <button className="button-secondary" type="button" onClick={onNew} disabled={disabled}>
          Nuovo
        </button>
        <button className="button-secondary" type="button" onClick={onResetOrRemove} disabled={disabled}>
          {selectedModel.isBuiltIn ? "Ripristina integrato" : "Rimuovi dal catalogo"}
        </button>
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
          ? `${formatFileSize(state.localSizeBytes)} verificati in ${state.localDirectory}`
          : state?.verificationError ?? model.recommendedProfile}
      </small>
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

function GeneratedImageCard({ image }: { image: GeneratedImage }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  useEffect(() => {
    let isCancelled = false;
    let createdUrl: string | null = null;

    async function loadImage() {
      try {
        const url = await fetchImageObjectUrl(image.id);
        if (isCancelled) {
          URL.revokeObjectURL(url);
          return;
        }

        createdUrl = url;
        setObjectUrl(url);
      } catch {
        setObjectUrl(null);
      }
    }

    void loadImage();
    return () => {
      isCancelled = true;
      if (createdUrl) {
        URL.revokeObjectURL(createdUrl);
      }
    };
  }, [image.id]);

  return (
    <article className="generated-image-card">
      {objectUrl ? (
        <img src={objectUrl} alt={image.prompt} />
      ) : (
        <div className="generated-image-card__placeholder" role="status">Caricamento...</div>
      )}
      <div className="generated-image-card__body">
        <strong>Integrato</strong>
        <p>{image.prompt}</p>
        <small>
          {image.width}x{image.height} · {image.steps} step · {formatFileSize(image.fileSizeBytes)}
        </small>
      </div>
    </article>
  );
}

async function fetchImageObjectUrl(imageId: number): Promise<string> {
  const baseUrl = resolveBackendBaseUrl();
  const sessionToken = resolveBackendSessionToken();
  if (!baseUrl || !sessionToken) {
    throw new Error("Backend non disponibile.");
  }

  const headers = new Headers();
  headers.set(sessionToken.headerName, sessionToken.token);
  const response = await fetch(new URL(`/api/images/${imageId}/file`, baseUrl), { headers });
  if (!response.ok) {
    throw new Error("Immagine non disponibile.");
  }

  return URL.createObjectURL(await response.blob());
}

function createModelDraft(model: ImageModelCatalogEntry): ModelDraft {
  return {
    id: model.id,
    displayName: model.displayName,
    recommendedProfile: model.recommendedProfile,
    downloadUrl: model.downloadUrl,
    licenseLabel: model.licenseLabel,
    expectedSizeBytes: String(model.expectedSizeBytes),
    requiredFiles: model.requiredFiles.join(", "),
    sha256: model.sha256
  };
}

function createEmptyModelDraft(id: string): ModelDraft {
  return {
    id,
    displayName: "Modello personalizzato",
    recommendedProfile: "Profilo personalizzato",
    downloadUrl: "https://huggingface.co/",
    licenseLabel: "Verificare licenza upstream",
    expectedSizeBytes: "0",
    requiredFiles: "model.onnx",
    sha256: ""
  };
}

function createModelRequest(draft: ModelDraft): ImageModelCatalogEntryRequest {
  return {
    id: draft.id.trim(),
    displayName: draft.displayName.trim(),
    recommendedProfile: draft.recommendedProfile.trim(),
    downloadUrl: draft.downloadUrl.trim(),
    licenseLabel: draft.licenseLabel.trim(),
    expectedSizeBytes: Number(draft.expectedSizeBytes.trim()) || 0,
    requiredFiles: draft.requiredFiles
      .split(",")
      .map((file) => file.trim())
      .filter(Boolean),
    sha256: draft.sha256.trim()
  };
}
