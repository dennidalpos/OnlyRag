import { ImageCanvasEditor } from "./images/ImageCanvasEditor";
import { ImageConsentDialog } from "./images/ImageConsentDialog";
import { ImageGalleryGrid } from "./images/ImageGalleryGrid";
import { ImageGeneratorControls } from "./images/ImageGeneratorControls";
import { ImageHardwareStatusHeader } from "./images/ImageHardwareStatusHeader";
import { ImageModelCatalogModal } from "./images/ImageModelCatalogModal";
import { resolveGenerationProfile } from "./images/imageTypes";
import { ProgressBar } from "./ProgressBar";
import { useImagesSectionController, useImageObjectUrl } from "./useImagesSectionController";

export { useImageObjectUrl };

export function ImagesSection() {
  const ctrl = useImagesSectionController();

  if (ctrl.isLoading) {
    return (
      <div className="section-layout" role="status">
        <div className="empty-state">Caricamento impostazioni e modelli immagini...</div>
      </div>
    );
  }

  return (
    <div className="images-section-layout">
      <ImageHardwareStatusHeader
        runtimeStatus={ctrl.runtimeStatus}
        selectedModel={ctrl.selectedModel}
        selectedModelState={ctrl.selectedModelState}
        onOpenSettings={() => ctrl.setIsSettingsOpen(true)}
        isModelActionRunning={ctrl.isModelActionRunning}
      />

      {ctrl.feedback && (
        <div className={`feedback-alert feedback-alert--${ctrl.feedback.tone}`} role="alert">
          {ctrl.feedback.message}
        </div>
      )}

      {ctrl.isModelActionRunning && (
        <div className="model-progress-bar">
          <span>{ctrl.modelActionMessage ?? "Download modello in corso..."}</span>
          <ProgressBar label={ctrl.modelActionMessage ?? "Download modello in corso..."} value={0} indeterminate />
        </div>
      )}

      <div className="images-workspace">
        <ImageGeneratorControls
          selectedModel={ctrl.selectedModel}
          prompt={ctrl.prompt}
          onPromptChange={ctrl.setPrompt}
          negativePrompt={ctrl.negativePrompt}
          onNegativePromptChange={ctrl.setNegativePrompt}
          width={ctrl.width}
          height={ctrl.height}
          onSizeChange={(w, h) => { ctrl.setWidth(w); ctrl.setHeight(h); }}
          generationProfile={ctrl.generationProfile}
          onGenerationProfileChange={(prof) => {
            ctrl.setGenerationProfile(prof);
            if (prof !== "custom") {
              const res = resolveGenerationProfile(ctrl.settings.selectedModelId, prof);
              ctrl.setSteps(res.steps);
              ctrl.setGuidanceScale(res.guidanceScale);
            }
          }}
          steps={ctrl.steps}
          onStepsChange={(s) => { ctrl.setSteps(s); ctrl.setGenerationProfile("custom"); }}
          seed={ctrl.seed}
          onSeedChange={ctrl.setSeed}
          guidanceScale={ctrl.guidanceScale}
          onGuidanceScaleChange={(g) => { ctrl.setGuidanceScale(g); ctrl.setGenerationProfile("custom"); }}
          canGenerate={ctrl.canGenerate}
          isGenerating={ctrl.isGenerating}
          onGenerate={ctrl.handleGenerate}
        />

        <div className="images-canvas-panel">
          <div className="canvas-toolbar">
            <div className="canvas-toolbar__tools">
              <button
                type="button"
                className={`button-secondary ${ctrl.activeTool === "move" ? "button-secondary--active" : ""}`}
                aria-pressed={ctrl.activeTool === "move"}
                onClick={() => ctrl.setActiveTool("move")}
                title="Seleziona e trascina elementi sull'immagine"
              >
                Sposta
              </button>
              <button
                type="button"
                className={`button-secondary ${ctrl.activeTool === "crop" ? "button-secondary--active" : ""}`}
                aria-pressed={ctrl.activeTool === "crop"}
                onClick={() => ctrl.setActiveTool("crop")}
                title="Trascina sull'immagine per selezionare l'area di ritaglio"
              >
                Ritaglio
              </button>
              <button
                type="button"
                className={`button-secondary ${ctrl.activeTool === "arrow" ? "button-secondary--active" : ""}`}
                aria-pressed={ctrl.activeTool === "arrow"}
                onClick={() => ctrl.setActiveTool("arrow")}
                title="Trascina sull'immagine per tracciare una freccia della lunghezza desiderata"
              >
                🏹 Freccia
              </button>
              <button
                type="button"
                className={`button-secondary ${ctrl.activeTool === "text" || ctrl.isAddingText ? "button-secondary--active" : ""}`}
                aria-pressed={ctrl.activeTool === "text" || ctrl.isAddingText}
                onClick={() => {
                  const nextState = !ctrl.isAddingText;
                  ctrl.setIsAddingText(nextState);
                  if (nextState) {
                    ctrl.setActiveTool("text");
                  } else if (ctrl.activeTool === "text") {
                    ctrl.setActiveTool("move");
                  }
                }}
                title="Mostra/nascondi il pannello per inserire e modificare il testo"
              >
                Testo overlay
              </button>
            </div>

            <div className="canvas-toolbar__actions">
              {ctrl.activeTool === "crop" && ctrl.editState.crop && ctrl.editState.crop.width > 1 && ctrl.editState.crop.height > 1 && (
                <button type="button" className="button-primary" onClick={ctrl.handleSaveEditedImage} disabled={ctrl.isSaving} title="Applica il ritaglio e salva l'immagine">
                  ✂️ Applica Ritaglio
                </button>
              )}
              {(ctrl.editState.crop || ctrl.editState.textLayers.length > 0 || ctrl.editState.arrowLayers.length > 0 || Boolean(ctrl.textInput.trim())) && ctrl.activeTool !== "crop" && (
                <button type="button" className="button-primary" onClick={ctrl.handleSaveEditedImage} disabled={ctrl.isSaving} title="Salva l'immagine modificata come un nuovo file">
                  {ctrl.isSaving ? "Salvataggio..." : "Salva come nuova immagine"}
                </button>
              )}
              <button type="button" className="button-secondary" onClick={ctrl.handleOpenFolder} title="Apri la cartella locale delle immagini generate">
                📂 Apri cartella
              </button>
              {ctrl.selectedImage && (
                <button type="button" className="button-danger" onClick={ctrl.handleDeleteSelectedImage} disabled={ctrl.isDeletingImage} title="Elimina l'immagine selezionata dal disco e dal database">
                  Elimina immagine
                </button>
              )}
            </div>
          </div>

          {ctrl.activeTool === "arrow" && (
            <div className="text-layer-editor">
              <span>Colore freccia:</span>
              <input type="color" value={ctrl.arrowColor} onChange={(e) => ctrl.setArrowColor(e.target.value)} title="Colore freccia" />
              <span>Spessore:</span>
              <input
                type="number"
                min={2}
                max={20}
                value={ctrl.arrowWidth}
                onChange={(e) => ctrl.setArrowWidth(Number(e.target.value))}
                title="Spessore linea (px)"
              />
              <span className="editor-hint">💡 Trascina sull'immagine per inserire una freccia della lunghezza desiderata.</span>
              {ctrl.editState.arrowLayers.length > 0 && (
                <button type="button" className="button-secondary" onClick={ctrl.handleClearArrows} title="Rimuovi tutte le frecce">
                  Rimuovi frecce ({ctrl.editState.arrowLayers.length})
                </button>
              )}
            </div>
          )}

          {ctrl.isAddingText && (
            <div className="text-layer-editor">
              <input
                type="text"
                value={ctrl.textInput}
                onChange={(e) => ctrl.setTextInput(e.target.value)}
                placeholder="Inserisci testo da applicare..."
                aria-label="Testo overlay"
              />
              <input type="color" value={ctrl.textColor} onChange={(e) => ctrl.setTextColor(e.target.value)} title="Colore testo" />
              <input
                type="number"
                min={12}
                max={120}
                value={ctrl.textSize}
                onChange={(e) => ctrl.setTextSize(Number(e.target.value))}
                title="Dimensione font (px)"
              />
              {ctrl.selectedTextId ? (
                <>
                  <button type="button" className="button-primary" onClick={ctrl.handleUpdateTextLayer} title="Aggiorna il testo selezionato con il nuovo contenuto">
                    Aggiorna
                  </button>
                  <button type="button" className="button-secondary" onClick={ctrl.handleAddTextLayer} title="Aggiungi come nuovo layer di testo separato">
                    Nuovo
                  </button>
                  <button type="button" className="button-danger" onClick={ctrl.handleDeleteTextLayer} title="Elimina il testo selezionato">
                    Elimina testo
                  </button>
                  <button type="button" className="button-secondary" onClick={ctrl.handleDeselectText} title="Deseleziona il testo">
                    ✕
                  </button>
                </>
              ) : (
                <button type="button" className="button-primary" onClick={ctrl.handleAddTextLayer} title="Aggiungi testo sull'immagine">
                  Aggiungi
                </button>
              )}
            </div>
          )}

          <ImageCanvasEditor
            selectedImage={ctrl.selectedImage}
            objectUrl={ctrl.selectedObjectUrl}
            editState={ctrl.editState}
            activeTool={ctrl.activeTool}
            selectedTextId={ctrl.selectedTextId}
            selectedArrowId={ctrl.selectedArrowId}
            previewRef={ctrl.previewRef}
            canUndo={ctrl.pastEdits.length > 0}
            canRedo={ctrl.futureEdits.length > 0}
            onUndo={ctrl.handleUndo}
            onRedo={ctrl.handleRedo}
            onRemoveCrop={ctrl.handleRemoveCrop}
            onDeleteSelectedArrow={ctrl.handleDeleteSelectedArrow}
            onDeleteSelectedText={ctrl.handleDeleteSelectedText}
            onResetEdits={ctrl.handleResetEdits}
            onPreviewPointerDown={ctrl.handlePreviewPointerDown}
            onPreviewPointerMove={ctrl.handlePreviewPointerMove}
            onPreviewPointerUp={ctrl.handlePreviewPointerUp}
            onTextPointerDown={ctrl.handleTextPointerDown}
            onArrowClick={(id) => ctrl.setSelectedArrowId(id)}
            onCopyPrompt={ctrl.handleCopyPrompt}
            onDownloadImage={ctrl.handleDownloadImage}
            onPrevImage={ctrl.handleSelectPrevImage}
            onNextImage={ctrl.handleSelectNextImage}
            hasPrevImage={ctrl.selectedImageIndex > 0}
            hasNextImage={ctrl.selectedImageIndex >= 0 && ctrl.selectedImageIndex < ctrl.images.length - 1}
          />

          <ImageGalleryGrid
            images={ctrl.images}
            selectedImageId={ctrl.selectedImage?.id ?? null}
            onSelectImage={(id) => ctrl.handleSelectImage(id)}
            onDownloadImage={ctrl.handleDownloadImage}
            onDeleteImage={(img) => ctrl.handleDeleteImage(img)}
            onCopyPrompt={ctrl.handleCopyPrompt}
          />
        </div>
      </div>

      <ImageModelCatalogModal
        isOpen={ctrl.isSettingsOpen}
        isMaximized={ctrl.isMaximized}
        modalRef={ctrl.settingsModalRef}
        settings={ctrl.settings}
        catalog={ctrl.catalog}
        modelStates={ctrl.modelStates}
        selectedModel={ctrl.selectedModel}
        selectedModelState={ctrl.selectedModelState}
        isSaving={ctrl.isSaving}
        isModelActionRunning={ctrl.isModelActionRunning}
        onClose={() => ctrl.setIsSettingsOpen(false)}
        onToggleMaximize={ctrl.toggleMaximized}
        onSaveSettings={ctrl.handleSaveSettings}
        onAskConsent={(modelId) => ctrl.setPendingConsentModelId(modelId)}
        onDeleteModel={ctrl.handleDeleteModel}
        onDeleteCatalogModel={ctrl.handleDeleteCatalogModel}
        onUpsertCatalogModel={ctrl.handleUpsertCatalogModel}
      />

      <ImageConsentDialog
        pendingConsentModelId={ctrl.pendingConsentModelId}
        consentModel={ctrl.consentModel}
        consentModalRef={ctrl.consentModalRef}
        onConfirm={ctrl.handleDownloadModel}
        onCancel={() => ctrl.setPendingConsentModelId(null)}
      />
    </div>
  );
}
