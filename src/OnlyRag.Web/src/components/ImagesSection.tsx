import { useEffect, useMemo, useRef, useState, type FormEvent, type PointerEvent } from "react";
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
import { ImageCanvasEditor } from "./images/ImageCanvasEditor";
import { ImageConsentDialog } from "./images/ImageConsentDialog";
import { ImageGalleryGrid } from "./images/ImageGalleryGrid";
import { ImageGeneratorControls } from "./images/ImageGeneratorControls";
import { ImageHardwareStatusHeader } from "./images/ImageHardwareStatusHeader";
import { ImageModelCatalogModal } from "./images/ImageModelCatalogModal";
import {
  createEmptyEditState,
  defaultModelId,
  defaultSettings,
  resolveGenerationProfile,
  type ArrowLayer,
  type EditTool,
  type Feedback,
  type GenerationProfile,
  type ImageEditState,
  type PromptLanguage,
  type TextLayer
} from "./images/imageTypes";
import { ProgressBar } from "./ProgressBar";
import { useModalFocusTrap } from "./useModalFocusTrap";
import { useModalMaximize } from "./useModalMaximize";

export function ImagesSection() {
  const [settings, setSettings] = useState<ImageGenerationSettings>(defaultSettings);
  const [runtimeStatus, setRuntimeStatus] = useState<ImageGenerationRuntimeStatus | null>(null);
  const [catalog, setCatalog] = useState<ImageModelCatalogEntry[]>([]);
  const [modelStates, setModelStates] = useState<ImageModelLocalState[]>([]);
  const [pendingConsentModelId, setPendingConsentModelId] = useState<string | null>(null);
  const [images, setImages] = useState<GeneratedImage[]>([]);
  const [selectedImageId, setSelectedImageId] = useState<number | null>(null);
  const [prompt, setPrompt] = useState("");
  const [promptLanguage, setPromptLanguage] = useState<PromptLanguage>("en");
  const [negativePrompt, setNegativePrompt] = useState("");
  const [width, setWidth] = useState(1024);
  const [height, setHeight] = useState(1024);
  const [generationProfile, setGenerationProfile] = useState<GenerationProfile>("balanced");
  const [steps, setSteps] = useState(resolveGenerationProfile(defaultModelId, "balanced").steps);
  const [batchSize, setBatchSize] = useState(resolveGenerationProfile(defaultModelId, "balanced").batchSize);
  const [guidanceScale, setGuidanceScale] = useState("");
  const [seed, setSeed] = useState("");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [isGenerating, setIsGenerating] = useState(false);
  const [isDeletingImage, setIsDeletingImage] = useState(false);
  const [isModelActionRunning, setIsModelActionRunning] = useState(false);
  const [modelActionMessage, setModelActionMessage] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<Feedback | null>(null);

  // Edit state for Canvas Editor
  const [activeTool, setActiveTool] = useState<EditTool>("move");
  const [editState, setEditState] = useState<ImageEditState>(createEmptyEditState());
  const [isAddingText, setIsAddingText] = useState(false);
  const [textInput, setTextInput] = useState("");
  const [textColor, setTextColor] = useState("#ffffff");
  const [textSize, setTextSize] = useState(36);
  const [selectedTextId, setSelectedTextId] = useState<number | null>(null);

  // Arrow controls
  const [arrowColor, setArrowColor] = useState("#ef4444");
  const [arrowWidth, setArrowWidth] = useState(4);
  const [selectedArrowId, setSelectedArrowId] = useState<number | null>(null);

  const [dragTarget, setDragTarget] = useState<"crop" | "text" | "arrow" | null>(null);
  const [dragStart, setDragStart] = useState<{ x: number; y: number } | null>(null);
  const [initialDragState, setInitialDragState] = useState<{ x: number; y: number } | null>(null);

  const previewRef = useRef<HTMLDivElement | null>(null);
  const settingsModalRef = useRef<HTMLDivElement | null>(null);
  const consentModalRef = useRef<HTMLDivElement | null>(null);

  useModalFocusTrap(settingsModalRef, isSettingsOpen);
  useModalFocusTrap(consentModalRef, Boolean(pendingConsentModelId));
  const { isMaximized, toggleMaximized } = useModalMaximize();

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
  const selectedObjectUrl = useImageObjectUrl(selectedImage?.id ?? null);
  const canGenerate = Boolean(prompt.trim()) && Boolean(selectedModelState?.isVerified);

  useEffect(() => {
    if (generationProfile !== "custom") {
      const profile = resolveGenerationProfile(settings.selectedModelId, generationProfile);
      setSteps(profile.steps);
      setBatchSize(profile.batchSize);
    }
  }, [generationProfile, settings.selectedModelId]);

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
    setEditState(createEmptyEditState());
    setSelectedTextId(null);
  }, [selectedImageId]);

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
        setRuntimeStatus(loadedRuntimeStatus);
        setCatalog(loadedCatalog);
        setModelStates(loadedModelStates);
        setImages(generatedImages);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({
            tone: "error",
            message: error instanceof Error ? error.message : "Impossibile caricare le impostazioni immagini."
          });
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

  async function handleSaveSettings(newSettings: ImageGenerationSettings) {
    setIsSaving(true);
    setFeedback(null);
    try {
      const updated = await apiRequest<ImageGenerationSettings>("/api/settings/image-generation", {
        method: "PUT",
        body: JSON.stringify(newSettings)
      });
      setSettings(updated);
      await refreshModelsAndStatus();
      setFeedback({ tone: "success", message: "Impostazioni immagini salvate." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Errore durante il salvataggio."
      });
    } finally {
      setIsSaving(false);
    }
  }

  async function refreshModelsAndStatus() {
    const [status, cat, states] = await Promise.all([
      apiRequest<ImageGenerationRuntimeStatus>("/api/images/runtime/status"),
      apiRequest<ImageModelCatalogEntry[]>("/api/images/models/catalog"),
      apiRequest<ImageModelLocalState[]>("/api/images/models")
    ]);
    setRuntimeStatus(status);
    setCatalog(cat);
    setModelStates(states);
  }

  async function handleDownloadModel(modelId: string) {
    setIsModelActionRunning(true);
    setModelActionMessage("Download modello in corso...");
    setFeedback(null);
    setPendingConsentModelId(null);
    try {
      const res = await apiRequest<ImageModelDownloadResponse>(`/api/images/models/${modelId}/download`, {
        method: "POST",
        body: JSON.stringify({ consentConfirmed: true })
      });
      await refreshModelsAndStatus();
      setFeedback({ tone: "success", message: res.message });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Download fallito."
      });
    } finally {
      setIsModelActionRunning(false);
      setModelActionMessage(null);
    }
  }

  async function handleDeleteModel(modelId: string) {
    if (!window.confirm("Rimuovere i file locali del modello?")) return;
    setIsModelActionRunning(true);
    setFeedback(null);
    try {
      await apiRequest(`/api/images/models/${modelId}`, { method: "DELETE" });
      await refreshModelsAndStatus();
      setFeedback({ tone: "success", message: "Modello rimosso dal disco." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Impossibile rimuovere il modello."
      });
    } finally {
      setIsModelActionRunning(false);
    }
  }

  async function handleUpsertCatalogModel(request: ImageModelCatalogEntryRequest) {
    setIsModelActionRunning(true);
    setModelActionMessage("Aggiornamento catalogo modelli...");
    setFeedback(null);
    try {
      await apiRequest(`/api/images/models/catalog/${request.id}`, {
        method: "PUT",
        body: JSON.stringify(request)
      });
      await refreshModelsAndStatus();
      setFeedback({ tone: "success", message: `Modello '${request.displayName}' salvato nel catalogo.` });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Impossibile aggiornare il catalogo."
      });
    } finally {
      setIsModelActionRunning(false);
      setModelActionMessage(null);
    }
  }

  async function handleGenerate(event: FormEvent) {
    event.preventDefault();
    if (!canGenerate) {
      setFeedback({
        tone: "warning",
        message: "Scarica e verifica il modello prima di proseguire con la generazione."
      });
      return;
    }

    setIsGenerating(true);
    setFeedback(null);
    try {
      let finalPrompt = prompt.trim();
      if (promptLanguage !== "en" && finalPrompt) {
        try {
          const trans = await apiRequest<{ originalPrompt: string; translatedPrompt: string }>("/api/images/translate-prompt", {
            method: "POST",
            body: JSON.stringify({ prompt: finalPrompt, sourceLanguage: promptLanguage })
          });
          if (trans.translatedPrompt && trans.translatedPrompt !== finalPrompt) {
            finalPrompt = trans.translatedPrompt;
            setFeedback({ tone: "success", message: `Prompt tradotto in inglese: "${finalPrompt}"` });
          }
        } catch {
          // fallback to original if translation fails
        }
      }

      const result = await apiRequest<ImageGenerationResponse>("/api/images/generate", {
        method: "POST",
        body: JSON.stringify({
          prompt: finalPrompt,
          negativePrompt: negativePrompt.trim() || null,
          modelId: settings.selectedModelId,
          width,
          height,
          steps,
          batchSize,
          guidanceScale: guidanceScale ? Number(guidanceScale) : null,
          seed: seed ? Number(seed) : null
        })
      });
      await refreshModelsAndStatus();
      if (result.images.length > 0) {
        setImages((prev) => [...result.images, ...prev]);
        setSelectedImageId(result.images[0].id);
      }
      setFeedback((prev) => prev ?? { tone: "success", message: result.message });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Generazione fallita."
      });
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleOpenFolder() {
    try {
      const res = await apiRequest<OperationMessageResponse>("/api/images/open-folder", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
      setFeedback({ tone: "success", message: res.message });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Impossibile aprire la cartella."
      });
    }
  }

  async function handleDeleteSelectedImage() {
    if (!selectedImage || !window.confirm("Eliminare l'immagine selezionata?")) return;

    setIsDeletingImage(true);
    setFeedback(null);
    try {
      await apiRequest<GeneratedImage>(`/api/images/${selectedImage.id}`, { method: "DELETE" });
      const deletedId = selectedImage.id;
      setImages((prev) => prev.filter((img) => img.id !== deletedId));
      setSelectedImageId(null);
      setFeedback({ tone: "success", message: "Immagine eliminata." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Eliminazione fallita."
      });
    } finally {
      setIsDeletingImage(false);
    }
  }

  async function handleSaveEditedImage() {
    if (!selectedImage || !selectedObjectUrl) return;

    let currentEditState = editState;
    if (textInput.trim()) {
      const pendingLayer: TextLayer = {
        id: Date.now(),
        text: textInput.trim(),
        x: 10,
        y: 10,
        fontSize: textSize,
        color: textColor
      };
      currentEditState = {
        ...editState,
        textLayers: [...editState.textLayers, pendingLayer]
      };
    }

    setIsSaving(true);
    setFeedback(null);
    try {
      const edited = await renderEditedImageToPng(selectedObjectUrl, currentEditState);
      const newImg = await apiRequest<GeneratedImage>(`/api/images/${selectedImage.id}/edit`, {
        method: "POST",
        body: JSON.stringify({
          imageBase64: edited.base64,
          mimeType: "image/png",
          width: edited.width,
          height: edited.height,
          replaceOriginal: false
        })
      });

      setImages((prev) => [newImg, ...prev]);
      setSelectedImageId(newImg.id);
      setEditState(createEmptyEditState());
      setTextInput("");
      setIsAddingText(false);
      setSelectedArrowId(null);
      setFeedback({ tone: "success", message: "Modifica salvata come nuova immagine." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Salvataggio modifica fallito."
      });
    } finally {
      setIsSaving(false);
    }
  }

  // Pointer events for Canvas crop, text drag, and arrow drawing
  function handlePreviewPointerDown(event: PointerEvent<HTMLDivElement>) {
    if (!previewRef.current) return;
    const rect = previewRef.current.getBoundingClientRect();
    const xPct = ((event.clientX - rect.left) / rect.width) * 100;
    const yPct = ((event.clientY - rect.top) / rect.height) * 100;

    if (activeTool === "crop") {
      setDragTarget("crop");
      setDragStart({ x: xPct, y: yPct });
      setEditState((prev) => ({
        ...prev,
        crop: { x: xPct, y: yPct, width: 0, height: 0 }
      }));
    } else if (activeTool === "arrow") {
      const newArrow: ArrowLayer = {
        id: Date.now(),
        x1: xPct,
        y1: yPct,
        x2: xPct,
        y2: yPct,
        color: arrowColor,
        strokeWidth: arrowWidth
      };
      setDragTarget("arrow");
      setDragStart({ x: xPct, y: yPct });
      setSelectedArrowId(newArrow.id);
      setEditState((prev) => ({
        ...prev,
        arrowLayers: [...prev.arrowLayers, newArrow]
      }));
    }
  }

  function handlePreviewPointerMove(event: PointerEvent<HTMLDivElement>) {
    if (!dragTarget || !dragStart || !previewRef.current) return;
    const rect = previewRef.current.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const rawXPct = ((event.clientX - rect.left) / rect.width) * 100;
    const rawYPct = ((event.clientY - rect.top) / rect.height) * 100;
    const xPct = Math.max(0, Math.min(100, rawXPct));
    const yPct = Math.max(0, Math.min(100, rawYPct));

    if (dragTarget === "crop") {
      const startX = Math.max(0, Math.min(100, dragStart.x));
      const startY = Math.max(0, Math.min(100, dragStart.y));
      const minX = Math.min(startX, xPct);
      const minY = Math.min(startY, yPct);
      const w = Math.abs(xPct - startX);
      const h = Math.abs(yPct - startY);
      setEditState((prev) => ({
        ...prev,
        crop: {
          x: Math.max(0, Math.min(100, minX)),
          y: Math.max(0, Math.min(100, minY)),
          width: Math.max(0, Math.min(100 - minX, w)),
          height: Math.max(0, Math.min(100 - minY, h))
        }
      }));
    } else if (dragTarget === "arrow" && selectedArrowId) {
      setEditState((prev) => ({
        ...prev,
        arrowLayers: prev.arrowLayers.map((arrow) =>
          arrow.id === selectedArrowId
            ? { ...arrow, x2: xPct, y2: yPct }
            : arrow
        )
      }));
    } else if (dragTarget === "text" && selectedTextId && initialDragState) {
      const dx = rawXPct - dragStart.x;
      const dy = rawYPct - dragStart.y;
      setEditState((prev) => ({
        ...prev,
        textLayers: prev.textLayers.map((layer) =>
          layer.id === selectedTextId
            ? {
                ...layer,
                x: Math.max(5, Math.min(95, initialDragState.x + dx)),
                y: Math.max(5, Math.min(95, initialDragState.y + dy))
              }
            : layer
        )
      }));
    }
  }

  function handlePreviewPointerUp() {
    setDragTarget(null);
    setDragStart(null);
    setInitialDragState(null);
  }

  function handleTextPointerDown(event: PointerEvent<HTMLButtonElement>, layer: TextLayer) {
    event.stopPropagation();
    setSelectedTextId(layer.id);
    setTextInput(layer.text);
    setTextColor(layer.color);
    setTextSize(layer.fontSize);
    setIsAddingText(true);
    if (!previewRef.current) return;
    const rect = previewRef.current.getBoundingClientRect();
    const xPct = ((event.clientX - rect.left) / rect.width) * 100;
    const yPct = ((event.clientY - rect.top) / rect.height) * 100;
    setDragTarget("text");
    setDragStart({ x: xPct, y: yPct });
    setInitialDragState({ x: layer.x, y: layer.y });
  }

  function handleAddTextLayer() {
    if (!textInput.trim()) return;
    const newLayer: TextLayer = {
      id: Date.now(),
      text: textInput.trim(),
      x: 10,
      y: 10,
      fontSize: textSize,
      color: textColor
    };
    setEditState((prev) => ({ ...prev, textLayers: [...prev.textLayers, newLayer] }));
    setSelectedTextId(newLayer.id);
  }

  function handleUpdateTextLayer() {
    if (!selectedTextId || !textInput.trim()) return;
    setEditState((prev) => ({
      ...prev,
      textLayers: prev.textLayers.map((layer) =>
        layer.id === selectedTextId
          ? { ...layer, text: textInput.trim(), color: textColor, fontSize: textSize }
          : layer
      )
    }));
  }

  function handleDeleteTextLayer() {
    if (!selectedTextId) return;
    setEditState((prev) => ({
      ...prev,
      textLayers: prev.textLayers.filter((layer) => layer.id !== selectedTextId)
    }));
    setSelectedTextId(null);
    setTextInput("");
  }

  function handleDeselectText() {
    setSelectedTextId(null);
    setTextInput("");
  }

  function handleClearArrows() {
    setEditState((prev) => ({ ...prev, arrowLayers: [] }));
    setSelectedArrowId(null);
  }

  if (isLoading) {
    return (
      <div className="section-layout" role="status">
        <div className="empty-state">Caricamento impostazioni e modelli immagini...</div>
      </div>
    );
  }

  return (
    <div className="images-section-layout">
      <ImageHardwareStatusHeader
        runtimeStatus={runtimeStatus}
        selectedModel={selectedModel}
        selectedModelState={selectedModelState}
        onOpenSettings={() => setIsSettingsOpen(true)}
        isModelActionRunning={isModelActionRunning}
      />

      {feedback && (
        <div className={`feedback-alert feedback-alert--${feedback.tone}`} role="alert">
          {feedback.message}
        </div>
      )}

      {isModelActionRunning && (
        <div className="model-progress-bar">
          <span>{modelActionMessage ?? "Download modello in corso..."}</span>
          <ProgressBar label={modelActionMessage ?? "Download modello in corso..."} value={0} indeterminate />
        </div>
      )}

      <div className="images-workspace">
        <ImageGeneratorControls
          prompt={prompt}
          onPromptChange={setPrompt}
          promptLanguage={promptLanguage}
          onPromptLanguageChange={setPromptLanguage}
          negativePrompt={negativePrompt}
          onNegativePromptChange={setNegativePrompt}
          width={width}
          height={height}
          onSizeChange={(w, h) => { setWidth(w); setHeight(h); }}
          generationProfile={generationProfile}
          onGenerationProfileChange={setGenerationProfile}
          steps={steps}
          onStepsChange={(s) => { setSteps(s); setGenerationProfile("custom"); }}
          seed={seed}
          onSeedChange={setSeed}
          guidanceScale={guidanceScale}
          onGuidanceScaleChange={setGuidanceScale}
          canGenerate={canGenerate}
          isGenerating={isGenerating}
          onGenerate={handleGenerate}
        />

        <div className="images-canvas-panel">
          <div className="canvas-toolbar">
            <div className="canvas-toolbar__tools">
              <button
                type="button"
                className={`button-secondary ${activeTool === "move" ? "button-secondary--active" : ""}`}
                aria-pressed={activeTool === "move"}
                onClick={() => setActiveTool("move")}
                title="Seleziona e trascina elementi sull'immagine"
              >
                Sposta
              </button>
              <button
                type="button"
                className={`button-secondary ${activeTool === "crop" ? "button-secondary--active" : ""}`}
                aria-pressed={activeTool === "crop"}
                onClick={() => setActiveTool("crop")}
                title="Trascina sull'immagine per selezionare l'area di ritaglio"
              >
                Ritaglio
              </button>
              <button
                type="button"
                className={`button-secondary ${activeTool === "arrow" ? "button-secondary--active" : ""}`}
                aria-pressed={activeTool === "arrow"}
                onClick={() => setActiveTool("arrow")}
                title="Trascina sull'immagine per tracciare una freccia della lunghezza desiderata"
              >
                🏹 Freccia
              </button>
              <button
                type="button"
                className={`button-secondary ${isAddingText ? "button-secondary--active" : ""}`}
                aria-pressed={isAddingText}
                onClick={() => setIsAddingText(!isAddingText)}
                title="Mostra/nascondi il pannello per inserire e modificare il testo"
              >
                Testo overlay
              </button>
            </div>

            <div className="canvas-toolbar__actions">
              {activeTool === "crop" && editState.crop && editState.crop.width > 1 && editState.crop.height > 1 && (
                <button type="button" className="button-primary" onClick={handleSaveEditedImage} disabled={isSaving} title="Applica il ritaglio e salva l'immagine">
                  ✂️ Applica Ritaglio
                </button>
              )}
              {(editState.crop || editState.textLayers.length > 0 || editState.arrowLayers.length > 0 || Boolean(textInput.trim())) && activeTool !== "crop" && (
                <button type="button" className="button-primary" onClick={handleSaveEditedImage} disabled={isSaving} title="Salva l'immagine modificata come un nuovo file">
                  {isSaving ? "Salvataggio..." : "Salva come nuova immagine"}
                </button>
              )}
              <button type="button" className="button-secondary" onClick={handleOpenFolder} title="Apri la cartella locale delle immagini generate">
                📂 Apri cartella
              </button>
              {selectedImage && (
                <button type="button" className="button-danger" onClick={handleDeleteSelectedImage} disabled={isDeletingImage} title="Elimina l'immagine selezionata dal disco e dal database">
                  Elimina immagine
                </button>
              )}
            </div>
          </div>

          {activeTool === "arrow" && (
            <div className="text-layer-editor">
              <span>Colore freccia:</span>
              <input type="color" value={arrowColor} onChange={(e) => setArrowColor(e.target.value)} title="Colore freccia" />
              <span>Spessore:</span>
              <input
                type="number"
                min={2}
                max={20}
                value={arrowWidth}
                onChange={(e) => setArrowWidth(Number(e.target.value))}
                title="Spessore linea (px)"
              />
              <span className="editor-hint">💡 Trascina sull'immagine per inserire una freccia della lunghezza desiderata.</span>
              {editState.arrowLayers.length > 0 && (
                <button type="button" className="button-secondary" onClick={handleClearArrows} title="Rimuovi tutte le frecce">
                  Rimuovi frecce ({editState.arrowLayers.length})
                </button>
              )}
            </div>
          )}

          {isAddingText && (
            <div className="text-layer-editor">
              <input
                type="text"
                value={textInput}
                onChange={(e) => setTextInput(e.target.value)}
                placeholder="Inserisci testo da applicare..."
                aria-label="Testo overlay"
              />
              <input type="color" value={textColor} onChange={(e) => setTextColor(e.target.value)} title="Colore testo" />
              <input
                type="number"
                min={12}
                max={120}
                value={textSize}
                onChange={(e) => setTextSize(Number(e.target.value))}
                title="Dimensione font (px)"
              />
              {selectedTextId ? (
                <>
                  <button type="button" className="button-primary" onClick={handleUpdateTextLayer} title="Aggiorna il testo selezionato con il nuovo contenuto">
                    Aggiorna
                  </button>
                  <button type="button" className="button-secondary" onClick={handleAddTextLayer} title="Aggiungi come nuovo layer di testo separato">
                    Nuovo
                  </button>
                  <button type="button" className="button-danger" onClick={handleDeleteTextLayer} title="Elimina il testo selezionato">
                    Elimina testo
                  </button>
                  <button type="button" className="button-secondary" onClick={handleDeselectText} title="Deseleziona il testo">
                    ✕
                  </button>
                </>
              ) : (
                <button type="button" className="button-primary" onClick={handleAddTextLayer} title="Aggiungi testo sull'immagine">
                  Aggiungi
                </button>
              )}
            </div>
          )}

          <ImageCanvasEditor
            selectedImage={selectedImage}
            objectUrl={selectedObjectUrl}
            editState={editState}
            activeTool={activeTool}
            selectedTextId={selectedTextId}
            selectedArrowId={selectedArrowId}
            previewRef={previewRef}
            onPreviewPointerDown={handlePreviewPointerDown}
            onPreviewPointerMove={handlePreviewPointerMove}
            onPreviewPointerUp={handlePreviewPointerUp}
            onTextPointerDown={handleTextPointerDown}
            onArrowClick={(id) => setSelectedArrowId(id)}
          />

          <ImageGalleryGrid
            images={images}
            selectedImageId={selectedImage?.id ?? null}
            onSelectImage={(id) => setSelectedImageId(id)}
          />
        </div>
      </div>

      <ImageModelCatalogModal
        isOpen={isSettingsOpen}
        isMaximized={isMaximized}
        modalRef={settingsModalRef}
        settings={settings}
        catalog={catalog}
        modelStates={modelStates}
        selectedModel={selectedModel}
        selectedModelState={selectedModelState}
        isSaving={isSaving}
        isModelActionRunning={isModelActionRunning}
        onClose={() => setIsSettingsOpen(false)}
        onToggleMaximize={toggleMaximized}
        onSaveSettings={handleSaveSettings}
        onAskConsent={(modelId) => setPendingConsentModelId(modelId)}
        onDeleteModel={handleDeleteModel}
        onUpsertCatalogModel={handleUpsertCatalogModel}
      />

      <ImageConsentDialog
        pendingConsentModelId={pendingConsentModelId}
        consentModel={consentModel}
        consentModalRef={consentModalRef}
        onConfirm={handleDownloadModel}
        onCancel={() => setPendingConsentModelId(null)}
      />
    </div>
  );
}

function renderEditedImageToPng(objectUrl: string, editState: ImageEditState): Promise<{ base64: string; width: number; height: number }> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => {
      const crop = editState.crop ?? { x: 0, y: 0, width: 100, height: 100 };
      const sourceX = Math.round((img.naturalWidth * crop.x) / 100);
      const sourceY = Math.round((img.naturalHeight * crop.y) / 100);
      const sourceWidth = Math.max(1, Math.round((img.naturalWidth * crop.width) / 100));
      const sourceHeight = Math.max(1, Math.round((img.naturalHeight * crop.height) / 100));
      const canvas = document.createElement("canvas");
      canvas.width = sourceWidth;
      canvas.height = sourceHeight;
      const ctx = canvas.getContext("2d");
      if (!ctx) {
        reject(new Error("Canvas non disponibile."));
        return;
      }
      ctx.drawImage(img, sourceX, sourceY, sourceWidth, sourceHeight, 0, 0, sourceWidth, sourceHeight);

      // Render Arrow Layers onto canvas context
      for (const arrow of editState.arrowLayers) {
        const x1 = ((arrow.x1 - crop.x) / crop.width) * sourceWidth;
        const y1 = ((arrow.y1 - crop.y) / crop.height) * sourceHeight;
        const x2 = ((arrow.x2 - crop.x) / crop.width) * sourceWidth;
        const y2 = ((arrow.y2 - crop.y) / crop.height) * sourceHeight;

        ctx.strokeStyle = arrow.color;
        ctx.fillStyle = arrow.color;
        ctx.lineWidth = arrow.strokeWidth;
        ctx.lineCap = "round";

        ctx.beginPath();
        ctx.moveTo(x1, y1);
        ctx.lineTo(x2, y2);
        ctx.stroke();

        const angle = Math.atan2(y2 - y1, x2 - x1);
        const headLength = 16 + arrow.strokeWidth * 2;
        ctx.beginPath();
        ctx.moveTo(x2, y2);
        ctx.lineTo(x2 - headLength * Math.cos(angle - Math.PI / 6), y2 - headLength * Math.sin(angle - Math.PI / 6));
        ctx.lineTo(x2 - headLength * Math.cos(angle + Math.PI / 6), y2 - headLength * Math.sin(angle + Math.PI / 6));
        ctx.closePath();
        ctx.fill();
      }

      // Render Text Layers onto canvas context
      for (const layer of editState.textLayers) {
        if (!layer.text.trim()) continue;
        const x = ((layer.x - crop.x) / crop.width) * sourceWidth;
        const y = ((layer.y - crop.y) / crop.height) * sourceHeight;
        ctx.fillStyle = layer.color;
        ctx.font = `${layer.fontSize}px sans-serif`;
        ctx.textBaseline = "top";
        ctx.fillText(layer.text, x, y);
      }
      const dataUrl = canvas.toDataURL("image/png");
      resolve({
        base64: dataUrl.slice(dataUrl.indexOf(",") + 1),
        width: sourceWidth,
        height: sourceHeight
      });
    };
    img.onerror = () => reject(new Error("Immagine non caricata."));
    img.src = objectUrl;
  });
}

export function useImageObjectUrl(imageId: number | null) {
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
