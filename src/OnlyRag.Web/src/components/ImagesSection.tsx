import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import {
  apiRequest,
  resolveBackendBaseUrl,
  resolveBackendSessionToken,
  type GeneratedImage,
  type ImageGenerationResponse,
  type ImageGenerationRuntimeStatus,
  type ImageGenerationSettings,
  type ImageModelCatalogEntry,
  type ImageModelCatalogEntryRequest,
  type ImageModelDownloadResponse,
  type ImageModelLocalState,
  type OperationMessageResponse
} from "../api";
import { formatFileSize } from "./DocumentsSection.formatting";
import { ProgressBar } from "./ProgressBar";
import { useModalFocusTrap } from "./useModalFocusTrap";

const defaultModelId = "onlyrag-sdxl-turbo-directml";

const defaultSettings: ImageGenerationSettings = {
  selectedModelId: defaultModelId,
  requestTimeoutSeconds: 300,
  preferGpu: true,
  activeExecutionProvider: "CPU"
};

const promptCommands = [
  "Fotorealistico",
  "Illustrazione pulita",
  "Luce naturale",
  "Sfondo semplice"
];

const sizePresets = [
  { label: "Quadrata", width: 1024, height: 1024 },
  { label: "Verticale", width: 832, height: 1216 },
  { label: "Orizzontale", width: 1216, height: 832 }
];

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
  const [selectedImageId, setSelectedImageId] = useState<number | null>(null);
  const [prompt, setPrompt] = useState("");
  const [negativePrompt, setNegativePrompt] = useState("");
  const [width, setWidth] = useState(1024);
  const [height, setHeight] = useState(1024);
  const [steps, setSteps] = useState(8);
  const [batchSize, setBatchSize] = useState(1);
  const [seed, setSeed] = useState("");
  const [modelDraft, setModelDraft] = useState<ModelDraft>(() => createEmptyModelDraft(defaultModelId));
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isSavingModel, setIsSavingModel] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isDeletingImage, setIsDeletingImage] = useState(false);
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
  const selectedImage = useMemo(
    () => images.find((image) => image.id === selectedImageId) ?? images[0] ?? null,
    [images, selectedImageId]
  );
  const canGenerate = Boolean(prompt.trim()) && Boolean(selectedModelState?.isVerified);

  useEffect(() => {
    if (selectedModel) {
      setModelDraft(createModelDraft(selectedModel));
    }
  }, [selectedModel]);

  useEffect(() => {
    if (images.length === 0) {
      setSelectedImageId(null);
      return;
    }

    if (!selectedImageId || !images.some((image) => image.id === selectedImageId)) {
      setSelectedImageId(images[0].id);
    }
  }, [images, selectedImageId]);

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
        setSelectedImageId(generatedImages[0]?.id ?? null);
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
      setSelectedImageId(response.images[0]?.id ?? selectedImageId);
      setFeedback({ tone: "success", message: response.message });
      await refreshImageState();
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Generazione non riuscita." });
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleDeleteImage(image: GeneratedImage) {
    if (!window.confirm(`Eliminare definitivamente "${image.fileName}"?`)) return;
    setIsDeletingImage(true);
    setFeedback(null);
    try {
      await apiRequest<GeneratedImage>(`/api/images/${image.id}`, { method: "DELETE" });
      setImages((current) => current.filter((item) => item.id !== image.id));
      setFeedback({ tone: "success", message: "Immagine eliminata." });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Eliminazione immagine non riuscita." });
    } finally {
      setIsDeletingImage(false);
    }
  }

  async function handleOpenGeneratedFolder() {
    setFeedback(null);
    try {
      const response = await apiRequest<OperationMessageResponse>("/api/images/open-folder", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setFeedback({ tone: "success", message: response.message });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Cartella immagini non aperta." });
    }
  }

  function appendPromptCommand(command: string) {
    setPrompt((current) => {
      const trimmed = current.trim();
      return trimmed ? `${trimmed}, ${command.toLowerCase()}` : command;
    });
  }

  function applyPreset(preset: { width: number; height: number }) {
    setWidth(preset.width);
    setHeight(preset.height);
  }

  return (
    <div className="images-panel">
      {feedback && (
        <div className={`feedback-banner feedback-banner--${feedback.tone}`} role={feedback.tone === "error" ? "alert" : "status"}>
          {feedback.message}
        </div>
      )}

      <div className="images-toolbar">
        <ImageRuntimeSummary runtimeStatus={runtimeStatus} state={selectedModelState} />
        <div className="images-toolbar__actions">
          <button className="button-secondary" type="button" onClick={() => void refreshImageState()} disabled={isLoading}>
            Aggiorna
          </button>
          <button className="button-secondary" type="button" onClick={() => setIsSettingsOpen(true)}>
            Impostazioni
          </button>
        </div>
      </div>

      <div className="images-layout">
        <PromptPanel
          prompt={prompt}
          negativePrompt={negativePrompt}
          width={width}
          height={height}
          steps={steps}
          batchSize={batchSize}
          seed={seed}
          canGenerate={canGenerate}
          isGenerating={isGenerating}
          selectedModelState={selectedModelState}
          onPromptChange={setPrompt}
          onNegativePromptChange={setNegativePrompt}
          onWidthChange={setWidth}
          onHeightChange={setHeight}
          onStepsChange={setSteps}
          onBatchSizeChange={setBatchSize}
          onSeedChange={setSeed}
          onAppendCommand={appendPromptCommand}
          onApplyPreset={applyPreset}
          onSubmit={handleGenerate}
        />

        <EditorPanel
          image={selectedImage}
          images={images}
          isDeleting={isDeletingImage}
          onSelectImage={setSelectedImageId}
          onDeleteImage={(image) => void handleDeleteImage(image)}
          onOpenFolder={() => void handleOpenGeneratedFolder()}
        />
      </div>

      {isSettingsOpen && (
        <ImageSettingsModal
          settings={settings}
          savedSettings={savedSettings}
          runtimeStatus={runtimeStatus}
          catalog={catalog}
          modelStates={modelStates}
          selectedModel={selectedModel}
          selectedModelState={selectedModelState}
          modelDraft={modelDraft}
          consentModel={consentModel}
          isSaving={isSaving}
          isSavingModel={isSavingModel}
          isModelActionRunning={isModelActionRunning}
          modelActionMessage={modelActionMessage}
          onSettingsChange={setSettings}
          onModelDraftChange={setModelDraft}
          onSaveSettings={() => void handleSaveSettings()}
          onClose={() => setIsSettingsOpen(false)}
          onAskConsent={(modelId) => setPendingConsentModelId(modelId)}
          onCancelConsent={() => setPendingConsentModelId(null)}
          onDownloadConfirmed={(modelId) => void handleDownloadConfirmed(modelId)}
          onDeleteModel={(modelId) => void handleDeleteModel(modelId)}
          onSaveModel={() => void handleSaveModel()}
          onNewModel={() => setModelDraft(createEmptyModelDraft(`custom-${Date.now()}`))}
          onResetOrRemoveModel={() => void handleResetOrRemoveModel()}
        />
      )}
    </div>
  );
}

function PromptPanel({
  prompt,
  negativePrompt,
  width,
  height,
  steps,
  batchSize,
  seed,
  canGenerate,
  isGenerating,
  selectedModelState,
  onPromptChange,
  onNegativePromptChange,
  onWidthChange,
  onHeightChange,
  onStepsChange,
  onBatchSizeChange,
  onSeedChange,
  onAppendCommand,
  onApplyPreset,
  onSubmit
}: {
  prompt: string;
  negativePrompt: string;
  width: number;
  height: number;
  steps: number;
  batchSize: number;
  seed: string;
  canGenerate: boolean;
  isGenerating: boolean;
  selectedModelState: ImageModelLocalState | null;
  onPromptChange: (value: string) => void;
  onNegativePromptChange: (value: string) => void;
  onWidthChange: (value: number) => void;
  onHeightChange: (value: number) => void;
  onStepsChange: (value: number) => void;
  onBatchSizeChange: (value: number) => void;
  onSeedChange: (value: string) => void;
  onAppendCommand: (value: string) => void;
  onApplyPreset: (preset: { width: number; height: number }) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
}) {
  return (
    <section className="settings-card images-prompt-panel" aria-labelledby="images-prompt-title">
      <div className="settings-card__header">
        <h2 id="images-prompt-title">Crea immagine</h2>
      </div>
      <form className="images-generate-form" onSubmit={onSubmit}>
        <div className="images-command-row" aria-label="Comandi standard">
          {promptCommands.map((command) => (
            <button className="button-secondary" type="button" onClick={() => onAppendCommand(command)} key={command}>
              {command}
            </button>
          ))}
        </div>
        <label className="field-group" htmlFor="image-prompt">
          <span>Prompt</span>
          <textarea id="image-prompt" rows={8} value={prompt} onChange={(event) => onPromptChange(event.target.value)} />
        </label>
        <label className="field-group" htmlFor="image-negative-prompt">
          <span>Negative prompt</span>
          <textarea
            id="image-negative-prompt"
            rows={3}
            value={negativePrompt}
            onChange={(event) => onNegativePromptChange(event.target.value)}
          />
        </label>
        <div className="images-preset-row" aria-label="Formato">
          {sizePresets.map((preset) => (
            <button
              className={`button-secondary${width === preset.width && height === preset.height ? " button-secondary--active" : ""}`}
              type="button"
              onClick={() => onApplyPreset(preset)}
              key={preset.label}
            >
              {preset.label}
            </button>
          ))}
        </div>
        <div className="settings-grid settings-grid--four">
          <label className="field-group" htmlFor="image-width">
            <span>Larghezza</span>
            <input id="image-width" min={256} max={2048} step={8} type="number" value={width} onChange={(event) => onWidthChange(Number(event.target.value))} />
          </label>
          <label className="field-group" htmlFor="image-height">
            <span>Altezza</span>
            <input id="image-height" min={256} max={2048} step={8} type="number" value={height} onChange={(event) => onHeightChange(Number(event.target.value))} />
          </label>
          <label className="field-group" htmlFor="image-steps">
            <span>Step</span>
            <input id="image-steps" min={1} max={150} type="number" value={steps} onChange={(event) => onStepsChange(Number(event.target.value))} />
          </label>
          <label className="field-group" htmlFor="image-batch">
            <span>Batch</span>
            <input id="image-batch" min={1} max={4} type="number" value={batchSize} onChange={(event) => onBatchSizeChange(Number(event.target.value))} />
          </label>
          <label className="field-group" htmlFor="image-seed">
            <span>Seed</span>
            <input id="image-seed" inputMode="numeric" value={seed} placeholder="Automatico" onChange={(event) => onSeedChange(event.target.value)} />
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
  );
}

function EditorPanel({
  image,
  images,
  isDeleting,
  onSelectImage,
  onDeleteImage,
  onOpenFolder
}: {
  image: GeneratedImage | null;
  images: GeneratedImage[];
  isDeleting: boolean;
  onSelectImage: (id: number) => void;
  onDeleteImage: (image: GeneratedImage) => void;
  onOpenFolder: () => void;
}) {
  return (
    <section className="settings-card images-editor-panel" aria-labelledby="images-editor-title">
      <div className="settings-card__header">
        <h2 id="images-editor-title">Editor</h2>
        <button className="button-secondary" type="button" onClick={onOpenFolder}>
          Apri cartella
        </button>
      </div>
      <ImagePreview image={image} />
      {image ? (
        <div className="image-detail">
          <strong>{image.fileName}</strong>
          <p>{image.prompt}</p>
          <small>
            {image.width}x{image.height} · {image.steps} step · {formatFileSize(image.fileSizeBytes)}
          </small>
          <div className="settings-actions">
            <button className="button-danger" type="button" onClick={() => onDeleteImage(image)} disabled={isDeleting}>
              {isDeleting ? "Eliminazione..." : "Elimina"}
            </button>
          </div>
        </div>
      ) : (
        <div className="empty-state" role="status">
          <p>Nessuna immagine generata.</p>
        </div>
      )}
      {images.length > 0 && (
        <div className="images-gallery" aria-label="Gallery">
          {images.map((generated) => (
            <GeneratedImageCard
              image={generated}
              isSelected={image?.id === generated.id}
              onSelect={() => onSelectImage(generated.id)}
              key={generated.id}
            />
          ))}
        </div>
      )}
    </section>
  );
}

function ImageSettingsModal({
  settings,
  savedSettings,
  runtimeStatus,
  catalog,
  modelStates,
  selectedModel,
  selectedModelState,
  modelDraft,
  consentModel,
  isSaving,
  isSavingModel,
  isModelActionRunning,
  modelActionMessage,
  onSettingsChange,
  onModelDraftChange,
  onSaveSettings,
  onClose,
  onAskConsent,
  onCancelConsent,
  onDownloadConfirmed,
  onDeleteModel,
  onSaveModel,
  onNewModel,
  onResetOrRemoveModel
}: {
  settings: ImageGenerationSettings;
  savedSettings: ImageGenerationSettings;
  runtimeStatus: ImageGenerationRuntimeStatus | null;
  catalog: ImageModelCatalogEntry[];
  modelStates: ImageModelLocalState[];
  selectedModel: ImageModelCatalogEntry | null;
  selectedModelState: ImageModelLocalState | null;
  modelDraft: ModelDraft;
  consentModel: ImageModelCatalogEntry | null;
  isSaving: boolean;
  isSavingModel: boolean;
  isModelActionRunning: boolean;
  modelActionMessage: string | null;
  onSettingsChange: (settings: ImageGenerationSettings) => void;
  onModelDraftChange: (draft: ModelDraft) => void;
  onSaveSettings: () => void;
  onClose: () => void;
  onAskConsent: (modelId: string) => void;
  onCancelConsent: () => void;
  onDownloadConfirmed: (modelId: string) => void;
  onDeleteModel: (modelId: string) => void;
  onSaveModel: () => void;
  onNewModel: () => void;
  onResetOrRemoveModel: () => void;
}) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const hasDirtySettings = JSON.stringify(settings) !== JSON.stringify(savedSettings);
  useModalFocusTrap(modalRef, true, { onEscape: onClose });

  return (
    <div className="modal-backdrop">
      <div className="compare-modal image-settings-modal" role="dialog" aria-modal="true" aria-labelledby="image-settings-title" ref={modalRef} tabIndex={-1}>
        <div className="compare-modal__header">
          <div>
            <h3 id="image-settings-title">Impostazioni immagini</h3>
            <span>{runtimeStatus?.message ?? "Lettura stato immagini..."}</span>
          </div>
          <button className="button-secondary" type="button" onClick={onClose} aria-label="Chiudi impostazioni immagini">
            Chiudi
          </button>
        </div>
        <div className="image-settings-modal__body">
          <div className="settings-grid settings-grid--two">
            <label className="field-group" htmlFor="image-model">
              <span>Modello integrato</span>
              <select
                id="image-model"
                value={settings.selectedModelId}
                onChange={(event) => onSettingsChange({ ...settings, selectedModelId: event.target.value })}
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
                onChange={(event) => onSettingsChange({ ...settings, requestTimeoutSeconds: Number(event.target.value) })}
              />
            </label>
            <label className="toggle-row images-trust-row" htmlFor="image-prefer-gpu">
              <input
                id="image-prefer-gpu"
                type="checkbox"
                checked={settings.preferGpu}
                onChange={(event) => onSettingsChange({ ...settings, preferGpu: event.target.checked })}
              />
              <span>Preferisci GPU DirectML quando disponibile</span>
            </label>
          </div>
          <div className="settings-actions">
            <button type="button" onClick={onSaveSettings} disabled={isSaving || !hasDirtySettings}>
              {isSaving ? "Salvataggio..." : "Salva impostazioni"}
            </button>
          </div>

          {selectedModel && (
            <>
              <ModelReadiness
                model={selectedModel}
                state={selectedModelState}
                onAskConsent={() => onAskConsent(selectedModel.id)}
                onDelete={() => onDeleteModel(selectedModel.id)}
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
                onChange={onModelDraftChange}
                onSave={onSaveModel}
                onNew={onNewModel}
                onResetOrRemove={onResetOrRemoveModel}
              />
            </>
          )}

          {consentModel && (
            <div className="panel-note panel-note--warning" role="dialog" aria-labelledby="image-model-consent-title">
              <h3 id="image-model-consent-title">Conferma download modello</h3>
              <p>{consentModel.displayName}</p>
              <p>Licenza: {consentModel.licenseLabel}</p>
              <p>Dimensione prevista: {formatModelSize(consentModel.expectedSizeBytes)}</p>
              <p>Destinazione: {modelStates.find((state) => state.modelId === consentModel.id)?.localDirectory ?? "%LOCALAPPDATA%\\OnlyRag\\models\\images"}</p>
              <div className="settings-actions">
                <button type="button" onClick={() => onDownloadConfirmed(consentModel.id)} disabled={isModelActionRunning}>
                  Conferma e scarica
                </button>
                <button className="button-secondary" type="button" onClick={onCancelConsent} disabled={isModelActionRunning}>
                  Annulla
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ImageRuntimeSummary({
  runtimeStatus,
  state
}: {
  runtimeStatus: ImageGenerationRuntimeStatus | null;
  state: ImageModelLocalState | null;
}) {
  return (
    <div className={runtimeStatus?.isReady ? "image-status image-status--online" : "image-status image-status--offline"}>
      <strong>{runtimeStatus?.isReady ? "Motore immagini pronto" : "Motore immagini non pronto"}</strong>
      <span>{runtimeStatus?.executionProvider ?? "In lettura"}</span>
      <small>{runtimeStatus?.fallbackReason ?? runtimeStatus?.suggestion ?? state?.verificationError ?? runtimeStatus?.message ?? "Lettura stato immagini..."}</small>
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
    <details className="panel-note panel-note--info image-advanced-settings">
      <summary>Catalogo avanzato</summary>
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
    </details>
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
          ? `${formatFileSize(state.localSizeBytes)} pronti in ${state.localDirectory}`
          : `${state?.verificationError ?? model.recommendedProfile} · ${formatRemainingDownload(state, model)}`}
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

function ImagePreview({ image }: { image: GeneratedImage | null }) {
  const objectUrl = useImageObjectUrl(image?.id ?? null);

  if (!image) {
    return <div className="generated-image-preview generated-image-preview--empty" role="status">Nessuna immagine selezionata.</div>;
  }

  return objectUrl ? (
    <img className="generated-image-preview" src={objectUrl} alt={image.prompt} />
  ) : (
    <div className="generated-image-preview generated-image-preview--empty" role="status">Caricamento...</div>
  );
}

function GeneratedImageCard({
  image,
  isSelected,
  onSelect
}: {
  image: GeneratedImage;
  isSelected: boolean;
  onSelect: () => void;
}) {
  const objectUrl = useImageObjectUrl(image.id);

  return (
    <button
      className={`generated-image-card${isSelected ? " generated-image-card--selected" : ""}`}
      type="button"
      onClick={onSelect}
      aria-label={`Seleziona ${image.fileName}`}
    >
      {objectUrl ? (
        <img src={objectUrl} alt="" />
      ) : (
        <span className="generated-image-card__placeholder" role="status">Caricamento...</span>
      )}
      <span className="generated-image-card__body">
        <strong>{image.fileName}</strong>
        <small>{formatFileSize(image.fileSizeBytes)}</small>
      </span>
    </button>
  );
}

function useImageObjectUrl(imageId: number | null) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  useEffect(() => {
    let isCancelled = false;
    let createdUrl: string | null = null;

    async function loadImage() {
      if (!imageId) {
        setObjectUrl(null);
        return;
      }

      try {
        const url = await fetchImageObjectUrl(imageId);
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
  }, [imageId]);

  return objectUrl;
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

function formatRemainingDownload(state: ImageModelLocalState | null, model: ImageModelCatalogEntry): string {
  const expected = state?.expectedSizeBytes ?? model.expectedSizeBytes;
  const remaining = state?.remainingDownloadBytes ?? expected;
  if (expected <= 0) {
    return "Dimensione modello non dichiarata";
  }

  return `${formatFileSize(remaining)} rimanenti`;
}

function formatModelSize(bytes: number): string {
  return bytes > 0 ? formatFileSize(bytes) : "Dimensione non dichiarata";
}
