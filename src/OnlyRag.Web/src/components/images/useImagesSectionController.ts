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
  type ImagePromptTranslationResponse,
  type OperationMessageResponse
} from "../../api";
import { formatFileSize } from "../documents/DocumentsSection.formatting";
import {
  createEmptyEditState,
  defaultModelId,
  defaultSettings,
  getCompatiblePresets,
  resolveGenerationProfile,
  type ArrowLayer,
  type EditTool,
  type Feedback,
  type GenerationProfile,
  type ImageEditState,
  type TextLayer
} from "./imageTypes";
import { useModalFocusTrap } from "../common/useModalFocusTrap";
import { useModalMaximize } from "../common/useModalMaximize";

export function useImagesSectionController() {
  const [settings, setSettings] = useState<ImageGenerationSettings>(defaultSettings);
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
  const [generationProfile, setGenerationProfile] = useState<GenerationProfile>("balanced");
  const [steps, setSteps] = useState(resolveGenerationProfile(defaultModelId, "balanced").steps);
  const [batchSize, setBatchSize] = useState(resolveGenerationProfile(defaultModelId, "balanced").batchSize);
  const [guidanceScale, setGuidanceScale] = useState(resolveGenerationProfile(defaultModelId, "balanced").guidanceScale);
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
  const [pastEdits, setPastEdits] = useState<ImageEditState[]>([]);
  const [editState, setEditStateRaw] = useState<ImageEditState>(createEmptyEditState());
  const [futureEdits, setFutureEdits] = useState<ImageEditState[]>([]);
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
  const dragStateRef = useRef<{
    target: "crop" | "text" | "arrow";
    textId?: number;
    arrowId?: number;
    startX: number;
    startY: number;
    initialX: number;
    initialY: number;
  } | null>(null);

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

  const selectedImageIndex = useMemo(() => {
    return images.findIndex((img) => img.id === selectedImageId);
  }, [images, selectedImageId]);

  function pushEditState(nextState: ImageEditState | ((prev: ImageEditState) => ImageEditState)) {
    setEditStateRaw((prev) => {
      const updated = typeof nextState === "function" ? nextState(prev) : nextState;
      setPastEdits((past) => [...past, prev]);
      setFutureEdits([]);
      return updated;
    });
  }

  function setEditStateDirectly(nextState: ImageEditState) {
    setEditStateRaw(nextState);
    setPastEdits([]);
    setFutureEdits([]);
  }

  function handleUndo() {
    if (pastEdits.length === 0) return;
    const previous = pastEdits[pastEdits.length - 1];
    const newPast = pastEdits.slice(0, pastEdits.length - 1);
    setFutureEdits((future) => [editState, ...future]);
    setPastEdits(newPast);
    setEditStateRaw(previous);
  }

  function handleRedo() {
    if (futureEdits.length === 0) return;
    const next = futureEdits[0];
    const newFuture = futureEdits.slice(1);
    setPastEdits((past) => [...past, editState]);
    setFutureEdits(newFuture);
    setEditStateRaw(next);
  }

  function handleResetEdits() {
    pushEditState(createEmptyEditState());
    setSelectedTextId(null);
    setSelectedArrowId(null);
  }

  function handleRemoveCrop() {
    pushEditState((prev) => ({ ...prev, crop: null }));
  }

  function handleDeleteSelectedArrow() {
    if (selectedArrowId === null) return;
    pushEditState((prev) => ({
      ...prev,
      arrowLayers: prev.arrowLayers.filter((a) => a.id !== selectedArrowId)
    }));
    setSelectedArrowId(null);
  }

  function handleDeleteSelectedText() {
    if (selectedTextId === null) return;
    pushEditState((prev) => ({
      ...prev,
      textLayers: prev.textLayers.filter((t) => t.id !== selectedTextId)
    }));
    setSelectedTextId(null);
    setTextInput("");
  }

  function handleSelectPrevImage() {
    if (selectedImageIndex > 0) {
      setSelectedImageId(images[selectedImageIndex - 1].id);
    }
  }

  function handleSelectNextImage() {
    if (selectedImageIndex >= 0 && selectedImageIndex < images.length - 1) {
      setSelectedImageId(images[selectedImageIndex + 1].id);
    }
  }

  function handleCopyPrompt(promptText: string) {
    if (!promptText) return;
    navigator.clipboard.writeText(promptText).then(
      () => setFeedback({ tone: "success", message: "Prompt copiato negli appunti!" }),
      () => setFeedback({ tone: "warning", message: "Impossibile copiare il prompt." })
    );
  }

  function handleDownloadImage(img: GeneratedImage) {
    const url = `/api/images/${img.id}/file`;
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = img.fileName;
    anchor.target = "_blank";
    anchor.click();
  }

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent) {
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement || e.target instanceof HTMLSelectElement) {
        return;
      }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "z") {
        if (e.shiftKey) {
          e.preventDefault();
          handleRedo();
        } else {
          e.preventDefault();
          handleUndo();
        }
      } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "y") {
        e.preventDefault();
        handleRedo();
      } else if (e.key === "Delete" || e.key === "Backspace") {
        if (selectedArrowId !== null) {
          e.preventDefault();
          handleDeleteSelectedArrow();
        } else if (selectedTextId !== null) {
          e.preventDefault();
          handleDeleteSelectedText();
        }
      } else if (selectedTextId !== null && (e.key === "ArrowLeft" || e.key === "ArrowRight" || e.key === "ArrowUp" || e.key === "ArrowDown")) {
        e.preventDefault();
        const step = e.shiftKey ? 2.5 : 0.5;
        let dx = 0;
        let dy = 0;
        if (e.key === "ArrowLeft") dx = -step;
        if (e.key === "ArrowRight") dx = step;
        if (e.key === "ArrowUp") dy = -step;
        if (e.key === "ArrowDown") dy = step;

        pushEditState((prev) => ({
          ...prev,
          textLayers: prev.textLayers.map((layer) =>
            layer.id === selectedTextId
              ? {
                  ...layer,
                  x: Math.max(0, Math.min(95, layer.x + dx)),
                  y: Math.max(0, Math.min(95, layer.y + dy))
                }
              : layer
          )
        }));
      }
    }

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [pastEdits, futureEdits, editState, selectedArrowId, selectedTextId]);

  useEffect(() => {
    if (generationProfile !== "custom") {
      const profile = resolveGenerationProfile(settings.selectedModelId, generationProfile);
      setSteps(profile.steps);
      setBatchSize(profile.batchSize);
    }
  }, [generationProfile, settings.selectedModelId]);

  useEffect(() => {
    if (selectedModel) {
      const presets = getCompatiblePresets(selectedModel);
      if (presets.length > 0) {
        const isCurrentValid = presets.some((p) => p.width === width && p.height === height);
        if (!isCurrentValid) {
          setWidth(presets[0].width);
          setHeight(presets[0].height);
        }
      }
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
    setEditStateDirectly(createEmptyEditState());
    setSelectedTextId(null);
    setSelectedArrowId(null);
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
    const targetModel = catalog.find((m) => m.id === modelId);
    const targetState = modelStates.find((s) => s.modelId === modelId);
    const displayName = targetModel?.displayName ?? modelId;
    const sizeText = targetState && targetState.localSizeBytes > 0 ? formatFileSize(targetState.localSizeBytes) : null;

    let confirmMessage = `Confermi di voler eliminare i file di installazione del modello '${displayName}'?`;
    if (sizeText) {
      confirmMessage += `\n\nI file occupano ${sizeText} su disco. Eliminando i file libererai questo spazio.`;
    } else {
      confirmMessage += `\n\nQuesta operazione liberera lo spazio su disco occupato dal modello.`;
    }

    if (!window.confirm(confirmMessage)) return;

    setIsModelActionRunning(true);
    setFeedback(null);
    try {
      await apiRequest(`/api/images/models/${modelId}`, { method: "DELETE" });
      await refreshModelsAndStatus();
      setFeedback({
        tone: "success",
        message: `File di installazione del modello '${displayName}' eliminati dal disco. Spazio liberato.`
      });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Impossibile rimuovere i file del modello."
      });
    } finally {
      setIsModelActionRunning(false);
    }
  }

  async function handleDeleteCatalogModel(modelId: string) {
    const targetModel = catalog.find((m) => m.id === modelId);
    const targetState = modelStates.find((s) => s.modelId === modelId);
    const displayName = targetModel?.displayName ?? modelId;
    const hasFiles = Boolean(targetState?.isDownloaded || (targetState?.localSizeBytes ?? 0) > 0);
    const sizeText = targetState && targetState.localSizeBytes > 0 ? formatFileSize(targetState.localSizeBytes) : null;

    let confirmMessage = `Sei sicuro di voler rimuovere il modello '${displayName}' dal catalogo?`;
    if (hasFiles && sizeText) {
      confirmMessage += `\n\n⚠️ Il modello ha file di installazione locali presenti su disco (${sizeText}).\n\nDesideri eliminare anche i file di installazione locali per liberare spazio su disco?`;
    }

    if (!window.confirm(confirmMessage)) return;

    setIsModelActionRunning(true);
    setFeedback(null);
    try {
      if (hasFiles) {
        await apiRequest(`/api/images/models/${modelId}`, { method: "DELETE" });
      }
      await apiRequest(`/api/images/models/catalog/${modelId}`, { method: "DELETE" });
      await refreshModelsAndStatus();
      setFeedback({
        tone: "success",
        message: `Modello '${displayName}' rimosso dal catalogo${hasFiles ? " e file di installazione eliminati dal disco." : "."}`
      });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Impossibile rimuovere il modello dal catalogo."
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
      if (finalPrompt) {
        try {
          const trans = await apiRequest<ImagePromptTranslationResponse>("/api/images/translate-prompt", {
            method: "POST",
            body: JSON.stringify({ prompt: finalPrompt })
          });
          if (trans.wasTranslated && trans.translatedPrompt && trans.translatedPrompt !== finalPrompt) {
            finalPrompt = trans.translatedPrompt;
            setFeedback({ tone: "success", message: `🌐 Prompt tradotto in inglese: "${finalPrompt}"` });
          }
        } catch {
          // fallback to original prompt if translation service is offline
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
      setEditStateDirectly(createEmptyEditState());
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
      pushEditState((prev) => ({
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
      pushEditState((prev) => ({
        ...prev,
        arrowLayers: [...prev.arrowLayers, newArrow]
      }));
    }
  }

  function handlePreviewPointerMove(event: PointerEvent<HTMLDivElement>) {
    const drag = dragStateRef.current;
    const activeTarget = drag?.target ?? dragTarget;
    if (!activeTarget || !previewRef.current) return;

    const rect = previewRef.current.getBoundingClientRect();
    if (rect.width === 0 || rect.height === 0) return;

    const rawXPct = ((event.clientX - rect.left) / rect.width) * 100;
    const rawYPct = ((event.clientY - rect.top) / rect.height) * 100;
    const xPct = Math.max(0, Math.min(100, rawXPct));
    const yPct = Math.max(0, Math.min(100, rawYPct));

    if (activeTarget === "crop") {
      const startX = Math.max(0, Math.min(100, dragStart?.x ?? 0));
      const startY = Math.max(0, Math.min(100, dragStart?.y ?? 0));
      const minX = Math.min(startX, xPct);
      const minY = Math.min(startY, yPct);
      const w = Math.abs(xPct - startX);
      const h = Math.abs(yPct - startY);
      setEditStateRaw((prev) => ({
        ...prev,
        crop: {
          x: Math.max(0, Math.min(100, minX)),
          y: Math.max(0, Math.min(100, minY)),
          width: Math.max(0, Math.min(100 - minX, w)),
          height: Math.max(0, Math.min(100 - minY, h))
        }
      }));
    } else if (activeTarget === "arrow") {
      const activeArrowId = drag?.arrowId ?? selectedArrowId;
      if (!activeArrowId) return;
      setEditStateRaw((prev) => ({
        ...prev,
        arrowLayers: prev.arrowLayers.map((arrow) =>
          arrow.id === activeArrowId
            ? { ...arrow, x2: xPct, y2: yPct }
            : arrow
        )
      }));
    } else if (activeTarget === "text") {
      const activeTextId = drag?.textId ?? selectedTextId;
      const startX = drag?.startX ?? dragStart?.x ?? 0;
      const startY = drag?.startY ?? dragStart?.y ?? 0;
      const initX = drag?.initialX ?? initialDragState?.x ?? 0;
      const initY = drag?.initialY ?? initialDragState?.y ?? 0;
      if (!activeTextId) return;

      const dx = rawXPct - startX;
      const dy = rawYPct - startY;

      setEditStateRaw((prev) => ({
        ...prev,
        textLayers: prev.textLayers.map((layer) =>
          layer.id === activeTextId
            ? {
                ...layer,
                x: Math.max(0, Math.min(95, initX + dx)),
                y: Math.max(0, Math.min(95, initY + dy))
              }
            : layer
        )
      }));
    }
  }

  function handlePreviewPointerUp() {
    const isTextDrag = dragStateRef.current?.target === "text" || dragTarget === "text";
    if (isTextDrag) {
      pushEditState((prev) => ({ ...prev }));
    }
    dragStateRef.current = null;
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

    dragStateRef.current = {
      target: "text",
      textId: layer.id,
      startX: xPct,
      startY: yPct,
      initialX: layer.x,
      initialY: layer.y
    };

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
    pushEditState((prev) => ({ ...prev, textLayers: [...prev.textLayers, newLayer] }));
    setSelectedTextId(newLayer.id);
  }

  function handleUpdateTextLayer() {
    if (!selectedTextId || !textInput.trim()) return;
    pushEditState((prev) => ({
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
    pushEditState((prev) => ({
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
    pushEditState((prev) => ({ ...prev, arrowLayers: [] }));
    setSelectedArrowId(null);
  }

  function handleSelectImage(id: number) {
    setSelectedImageId(id);
  }

  function handleDeleteImage(img: GeneratedImage) {
    if (window.confirm(`Eliminare l'immagine "${img.fileName}"?`)) {
      apiRequest(`/api/images/${img.id}`, { method: "DELETE" }).then(() => {
        setImages((prev) => prev.filter((i) => i.id !== img.id));
        setFeedback({ tone: "success", message: "Immagine eliminata." });
      });
    }
  }

  return {
    settings,
    runtimeStatus,
    catalog,
    modelStates,
    pendingConsentModelId,
    images,
    selectedImage,
    selectedImageId,
    selectedObjectUrl,
    selectedModel,
    selectedModelState,
    consentModel,
    prompt,
    setPrompt,
    negativePrompt,
    setNegativePrompt,
    width,
    height,
    setWidth,
    setHeight,
    generationProfile,
    setGenerationProfile,
    steps,
    setSteps,
    guidanceScale,
    setGuidanceScale,
    seed,
    setSeed,
    isSettingsOpen,
    setIsSettingsOpen,
    isLoading,
    isSaving,
    isGenerating,
    isDeletingImage,
    isModelActionRunning,
    modelActionMessage,
    feedback,
    canGenerate,
    activeTool,
    setActiveTool,
    pastEdits,
    futureEdits,
    editState,
    isAddingText,
    setIsAddingText,
    textInput,
    setTextInput,
    textColor,
    setTextColor,
    textSize,
    setTextSize,
    selectedTextId,
    setSelectedTextId,
    arrowColor,
    setArrowColor,
    arrowWidth,
    setArrowWidth,
    selectedArrowId,
    setSelectedArrowId,
    previewRef,
    settingsModalRef,
    consentModalRef,
    isMaximized,
    toggleMaximized,
    selectedImageIndex,
    handleUndo,
    handleRedo,
    handleResetEdits,
    handleRemoveCrop,
    handleDeleteSelectedArrow,
    handleDeleteSelectedText,
    handleSelectPrevImage,
    handleSelectNextImage,
    handleCopyPrompt,
    handleDownloadImage,
    handleSaveSettings,
    handleDownloadModel,
    handleDeleteModel,
    handleDeleteCatalogModel,
    handleUpsertCatalogModel,
    handleGenerate,
    handleOpenFolder,
    handleDeleteSelectedImage,
    handleSaveEditedImage,
    handlePreviewPointerDown,
    handlePreviewPointerMove,
    handlePreviewPointerUp,
    handleTextPointerDown,
    handleAddTextLayer,
    handleUpdateTextLayer,
    handleDeleteTextLayer,
    handleDeselectText,
    handleClearArrows,
    handleSelectImage,
    handleDeleteImage,
    setPendingConsentModelId
  };
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
