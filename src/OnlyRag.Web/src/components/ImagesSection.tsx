import { useEffect, useMemo, useState, type FormEvent } from "react";
import {
  apiRequest,
  resolveBackendBaseUrl,
  resolveBackendSessionToken,
  type GeneratedImage,
  type ImageGenerationProviderStatus,
  type ImageGenerationResponse,
  type ImageGenerationSettings
} from "../api";
import { formatFileSize } from "./DocumentsSection.formatting";

const defaultSettings: ImageGenerationSettings = {
  provider: "automatic1111",
  automatic1111BaseUrl: "http://127.0.0.1:7860",
  comfyUiBaseUrl: "http://127.0.0.1:8188",
  requestTimeoutSeconds: 300,
  trustNonLocalEndpoint: false,
  automatic1111Model: null,
  comfyUiWorkflowJson: null
};

type Feedback = {
  tone: "success" | "error" | "warning";
  message: string;
};

export function ImagesSection() {
  const [settings, setSettings] = useState<ImageGenerationSettings>(defaultSettings);
  const [savedSettings, setSavedSettings] = useState<ImageGenerationSettings>(defaultSettings);
  const [statuses, setStatuses] = useState<ImageGenerationProviderStatus[]>([]);
  const [images, setImages] = useState<GeneratedImage[]>([]);
  const [prompt, setPrompt] = useState("");
  const [negativePrompt, setNegativePrompt] = useState("");
  const [model, setModel] = useState("");
  const [width, setWidth] = useState(1024);
  const [height, setHeight] = useState(1024);
  const [steps, setSteps] = useState(30);
  const [batchSize, setBatchSize] = useState(1);
  const [seed, setSeed] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  const activeStatus = useMemo(
    () => statuses.find((status) => status.provider === settings.provider) ?? null,
    [settings.provider, statuses]
  );
  const hasDirtySettings = JSON.stringify(settings) !== JSON.stringify(savedSettings);

  useEffect(() => {
    let isCancelled = false;

    async function load() {
      setIsLoading(true);
      try {
        const [loadedSettings, providerStatuses, generatedImages] = await Promise.all([
          apiRequest<ImageGenerationSettings>("/api/settings/image-generation"),
          apiRequest<ImageGenerationProviderStatus[]>("/api/images/providers/status"),
          apiRequest<GeneratedImage[]>("/api/images")
        ]);
        if (isCancelled) return;
        setSettings(loadedSettings);
        setSavedSettings(loadedSettings);
        setModel(loadedSettings.provider === "automatic1111" ? loadedSettings.automatic1111Model ?? "" : "");
        setStatuses(providerStatuses);
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

  async function refreshStatuses() {
    const providerStatuses = await apiRequest<ImageGenerationProviderStatus[]>("/api/images/providers/status");
    setStatuses(providerStatuses);
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
      await refreshStatuses();
      setFeedback({ tone: "success", message: "Impostazioni immagini salvate." });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Salvataggio non riuscito." });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleGenerate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!prompt.trim()) {
      setFeedback({ tone: "error", message: "Inserisci un prompt per generare immagini." });
      return;
    }

    setIsGenerating(true);
    setFeedback(null);
    try {
      const response = await apiRequest<ImageGenerationResponse>("/api/images/generate", {
        method: "POST",
        body: JSON.stringify({
          provider: settings.provider,
          prompt,
          negativePrompt: negativePrompt.trim() || null,
          model: model.trim() || null,
          width,
          height,
          steps,
          batchSize,
          seed: seed.trim() ? Number(seed) : null
        })
      });
      setImages((current) => [...response.images, ...current]);
      setFeedback({ tone: "success", message: response.message });
    } catch (error) {
      setFeedback({ tone: "error", message: error instanceof Error ? error.message : "Generazione non riuscita." });
    } finally {
      setIsGenerating(false);
    }
  }

  function updateProvider(provider: string) {
    setSettings((current) => ({ ...current, provider }));
    setModel(provider === "automatic1111" ? settings.automatic1111Model ?? "" : "");
  }

  return (
    <div className="images-panel">
      <div className="images-layout">
        <section className="settings-card images-control-panel" aria-labelledby="images-title">
          <div className="settings-card__header">
            <div>
              <h2 id="images-title">Generazione immagini</h2>
              <p>Automatic1111 e ComfyUI locali. Fooocus non è supportato nella v1 operativa.</p>
            </div>
            <button className="button-secondary" type="button" onClick={() => void refreshStatuses()} disabled={isLoading}>
              Verifica
            </button>
          </div>

          {feedback && (
            <div className={`feedback-banner feedback-banner--${feedback.tone}`} role={feedback.tone === "error" ? "alert" : "status"}>
              {feedback.message}
            </div>
          )}

          <div className="image-status-grid">
            {statuses.map((status) => (
              <div
                className={status.isReachable ? "image-status image-status--online" : "image-status image-status--offline"}
                key={status.provider}
              >
                <strong>{formatProvider(status.provider)}</strong>
                <span>{status.state}</span>
                <small>{status.message}</small>
              </div>
            ))}
          </div>

          <div className="settings-grid settings-grid--two">
            <label className="field-group" htmlFor="image-provider">
              <span>Provider</span>
              <select id="image-provider" value={settings.provider} onChange={(event) => updateProvider(event.target.value)}>
                <option value="automatic1111">Automatic1111</option>
                <option value="comfyui">ComfyUI</option>
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
            <label className="field-group" htmlFor="automatic1111-url">
              <span>Automatic1111 URL</span>
              <input
                id="automatic1111-url"
                value={settings.automatic1111BaseUrl}
                onChange={(event) => setSettings((current) => ({ ...current, automatic1111BaseUrl: event.target.value }))}
              />
            </label>
            <label className="field-group" htmlFor="comfy-url">
              <span>ComfyUI URL</span>
              <input
                id="comfy-url"
                value={settings.comfyUiBaseUrl}
                onChange={(event) => setSettings((current) => ({ ...current, comfyUiBaseUrl: event.target.value }))}
              />
            </label>
            <label className="field-group" htmlFor="automatic1111-model">
              <span>Checkpoint Automatic1111</span>
              <input
                id="automatic1111-model"
                value={settings.automatic1111Model ?? ""}
                placeholder="Opzionale"
                onChange={(event) =>
                  setSettings((current) => ({ ...current, automatic1111Model: event.target.value || null }))
                }
              />
            </label>
            <label className="toggle-row images-trust-row" htmlFor="image-trust-remote">
              <input
                id="image-trust-remote"
                type="checkbox"
                checked={settings.trustNonLocalEndpoint}
                onChange={(event) => setSettings((current) => ({ ...current, trustNonLocalEndpoint: event.target.checked }))}
              />
              <span>Consenti endpoint immagini non locali</span>
            </label>
            <label className="field-group images-workflow-field" htmlFor="comfy-workflow">
              <span>Workflow ComfyUI JSON</span>
              <textarea
                id="comfy-workflow"
                rows={5}
                value={settings.comfyUiWorkflowJson ?? ""}
                placeholder="Opzionale. Placeholder supportati: {{prompt}}, {{negative_prompt}}, {{model}}, {{width}}, {{height}}, {{steps}}, {{batch_size}}, {{seed}}"
                onChange={(event) =>
                  setSettings((current) => ({ ...current, comfyUiWorkflowJson: event.target.value || null }))
                }
              />
            </label>
          </div>

          <div className="settings-actions">
            <button type="button" onClick={() => void handleSaveSettings()} disabled={isSaving || !hasDirtySettings}>
              {isSaving ? "Salvataggio..." : "Salva impostazioni"}
            </button>
          </div>

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
              <label className="field-group" htmlFor="image-model">
                <span>{settings.provider === "comfyui" ? "Checkpoint/Modello" : "Modello richiesta"}</span>
                <input id="image-model" value={model} onChange={(event) => setModel(event.target.value)} />
              </label>
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
            {activeStatus && !activeStatus.isReachable && (
              <div className="panel-note panel-note--warning" role="status">
                <p>{activeStatus.suggestion ?? activeStatus.message}</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="submit" disabled={isGenerating || !prompt.trim()}>
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
        <strong>{formatProvider(image.provider)}</strong>
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

function formatProvider(provider: string): string {
  return provider === "comfyui" ? "ComfyUI" : "Automatic1111";
}

