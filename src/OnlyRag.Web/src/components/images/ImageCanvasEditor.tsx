import { useState, type PointerEvent, type RefObject } from "react";
import type { GeneratedImage } from "../../api";
import { isEditStateEmpty, type ArrowLayer, type EditTool, type ImageEditState, type TextLayer } from "./imageTypes";

type Props = {
  selectedImage: GeneratedImage | null;
  objectUrl: string | null;
  editState: ImageEditState;
  activeTool: EditTool;
  selectedTextId: number | null;
  selectedArrowId: number | null;
  previewRef: RefObject<HTMLDivElement | null>;
  canUndo: boolean;
  canRedo: boolean;
  onUndo: () => void;
  onRedo: () => void;
  onRemoveCrop: () => void;
  onDeleteSelectedArrow: () => void;
  onDeleteSelectedText: () => void;
  onResetEdits: () => void;
  onPreviewPointerDown: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerMove: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerUp: () => void;
  onTextPointerDown: (event: PointerEvent<HTMLButtonElement>, layer: TextLayer) => void;
  onArrowClick?: (id: number) => void;
  onCopyPrompt?: (prompt: string) => void;
  onDownloadImage?: (img: GeneratedImage) => void;
  onPrevImage?: () => void;
  onNextImage?: () => void;
  hasPrevImage?: boolean;
  hasNextImage?: boolean;
};

export function ImageCanvasEditor({
  selectedImage,
  objectUrl,
  editState,
  activeTool,
  selectedTextId,
  selectedArrowId,
  previewRef,
  canUndo,
  canRedo,
  onUndo,
  onRedo,
  onRemoveCrop,
  onDeleteSelectedArrow,
  onDeleteSelectedText,
  onResetEdits,
  onPreviewPointerDown,
  onPreviewPointerMove,
  onPreviewPointerUp,
  onTextPointerDown,
  onArrowClick,
  onCopyPrompt,
  onDownloadImage,
  onPrevImage,
  onNextImage,
  hasPrevImage,
  hasNextImage
}: Props) {
  const [maskOpacity, setMaskOpacity] = useState(0.75);
  const [brushPos, setBrushPos] = useState<{ x: number; y: number } | null>(null);

  if (!selectedImage) {
    return (
      <div className="generated-image-preview generated-image-preview--empty" role="status">
        🖼️ Nessuna immagine selezionata. Genera o seleziona un'immagine dalla galleria.
      </div>
    );
  }

  const hasEdits = !isEditStateEmpty(editState);

  function handlePointerMoveInternal(e: PointerEvent<HTMLDivElement>) {
    if (previewRef.current) {
      const rect = previewRef.current.getBoundingClientRect();
      setBrushPos({
        x: e.clientX - rect.left,
        y: e.clientY - rect.top
      });
    }
    onPreviewPointerMove(e);
  }

  function handlePointerLeave() {
    setBrushPos(null);
  }

  function handlePreviewPointerDownInternal(event: PointerEvent<HTMLDivElement>) {
    event.currentTarget.setPointerCapture(event.pointerId);
    onPreviewPointerDown(event);
  }

  function handlePreviewPointerUpInternal(event: PointerEvent<HTMLDivElement>) {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    onPreviewPointerUp();
  }

  return (
    <div className="canvas-editor-container">
      {/* Header bar with Image Info & Navigation Controls */}
      <div className="canvas-header-bar" style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px", flexWrap: "wrap", gap: "8px" }}>
        <div className="canvas-image-info">
          <strong>{selectedImage.fileName}</strong> ({selectedImage.width}x{selectedImage.height}px)
        </div>
        <div className="canvas-nav-actions" style={{ display: "flex", gap: "6px" }}>
          {onPrevImage && (
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={onPrevImage}
              disabled={!hasPrevImage}
              title="Immagine precedente nella galleria"
            >
              Precedente
            </button>
          )}
          {onNextImage && (
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={onNextImage}
              disabled={!hasNextImage}
              title="Immagine successiva nella galleria"
            >
              Successiva
            </button>
          )}
          {onCopyPrompt && (
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={() => onCopyPrompt(selectedImage.prompt)}
              title="Copia il prompt dell'immagine negli appunti"
            >
              Copia Prompt
            </button>
          )}
          {onDownloadImage && (
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={() => onDownloadImage(selectedImage)}
              title="Scarica l'immagine originale sul PC locale"
            >
              Scarica File
            </button>
          )}
        </div>
      </div>

      {/* Edit History Toolbar & Mask Opacity Control */}
      <div className="edit-history-toolbar" style={{ display: "flex", gap: "10px", marginBottom: "8px", flexWrap: "wrap", alignItems: "center", background: "#1e293b", padding: "6px 12px", borderRadius: "var(--radius-md)", border: "1px solid #334155" }}>
        <span style={{ fontSize: "12px", fontWeight: 600, color: "#cbd5e1", marginRight: "4px" }}>Storico:</span>
        <button
          type="button"
          className="button-secondary button-secondary--xs"
          onClick={onUndo}
          disabled={!canUndo}
          title="Annulla l'ultima modifica applicata (Ctrl+Z)"
        >
          Annulla
        </button>

        <button
          type="button"
          className="button-secondary button-secondary--xs"
          onClick={onRedo}
          disabled={!canRedo}
          title="Ripristina la modifica annullata (Ctrl+Y)"
        >
          Ripristina
        </button>

        {/* Mask Opacity Slider */}
        <div style={{ display: "flex", alignItems: "center", gap: "6px", marginLeft: "auto" }}>
          <label htmlFor="mask-opacity-slider" style={{ fontSize: "11px", fontWeight: 600, color: "#94a3b8" }}>
            Opacità Maschera:
          </label>
          <input
            id="mask-opacity-slider"
            type="range"
            min="0.1"
            max="1.0"
            step="0.05"
            value={maskOpacity}
            onChange={(e) => setMaskOpacity(parseFloat(e.target.value))}
            style={{ width: "70px", accentColor: "#38bdf8", cursor: "pointer" }}
            title="Regola l'opacità della maschera sul canvas"
          />
          <span style={{ fontSize: "11px", color: "#f8fafc", width: "32px" }}>{Math.round(maskOpacity * 100)}%</span>
        </div>

        {editState.crop && (
          <button
            type="button"
            className="button-danger button-danger--xs"
            onClick={onRemoveCrop}
            title="Rimuovi il rettangolo di ritaglio dal canvas"
          >
            Rimuovi Ritaglio
          </button>
        )}

        {selectedArrowId !== null && (
          <button
            type="button"
            className="button-danger button-danger--xs"
            onClick={onDeleteSelectedArrow}
            title="Elimina la freccia attualmente selezionata"
          >
            Elimina Freccia
          </button>
        )}

        {selectedTextId !== null && (
          <button
            type="button"
            className="button-danger button-danger--xs"
            onClick={onDeleteSelectedText}
            title="Elimina il testo attualmente selezionato"
          >
            Elimina Testo
          </button>
        )}

        {hasEdits && (
          <button
            type="button"
            className="button-secondary button-secondary--xs"
            onClick={onResetEdits}
            title="Azzera e cancella tutte le modifiche e sovrapposizioni sul canvas"
          >
            Azzera Modifiche
          </button>
        )}
      </div>

      {objectUrl ? (
        <div
          className={`generated-image-preview-frame generated-image-preview-frame--${activeTool}`}
          ref={previewRef}
          onPointerDown={handlePreviewPointerDownInternal}
          onPointerMove={handlePointerMoveInternal}
          onPointerUp={handlePreviewPointerUpInternal}
          onPointerCancel={handlePreviewPointerUpInternal}
          onPointerLeave={handlePointerLeave}
          style={{ position: "relative", opacity: 1 }}
        >
          {/* Floating Canvas Toolbar */}
          <div
            className="canvas-floating-toolbar"
            style={{
              position: "absolute",
              top: "12px",
              left: "50%",
              transform: "translateX(-50%)",
              zIndex: 30,
              display: "flex",
              gap: "6px",
              alignItems: "center",
              background: "rgba(15, 23, 42, 0.88)",
              backdropFilter: "blur(8px)",
              padding: "6px 14px",
              borderRadius: "20px",
              border: "1px solid rgba(255, 255, 255, 0.18)",
              boxShadow: "0 8px 24px rgba(0, 0, 0, 0.45)",
              fontSize: "0.8rem",
              color: "#f8fafc"
            }}
          >
            <span style={{ fontWeight: 700, color: "#38bdf8", marginRight: "4px" }}>
              {activeTool === "crop" ? "✂️ Ritaglio" : activeTool === "text" ? "💬 Testo" : activeTool === "arrow" ? "↗️ Freccia" : "🖐️ Seleziona"}
            </span>
            <div style={{ width: "1px", height: "16px", background: "#334155", margin: "0 4px" }} />
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={onUndo}
              disabled={!canUndo}
              title="Annulla (Ctrl+Z)"
              style={{ padding: "3px 9px", fontSize: "0.76rem" }}
            >
              ↩️ Annulla
            </button>
            <button
              type="button"
              className="button-secondary button-secondary--xs"
              onClick={onRedo}
              disabled={!canRedo}
              title="Ripristina (Ctrl+Y)"
              style={{ padding: "3px 9px", fontSize: "0.76rem" }}
            >
              ↪️ Ripristina
            </button>
            {hasEdits && (
              <button
                type="button"
                className="button-danger button-danger--xs"
                onClick={onResetEdits}
                title="Azzera tutte le sovrapposizioni e modifiche"
                style={{ padding: "3px 9px", fontSize: "0.76rem" }}
              >
                🗑️ Reset
              </button>
            )}
          </div>

          <img
            className="generated-image-preview"
            src={objectUrl}
            alt={selectedImage.prompt}
            draggable={false}
          />

          {/* Dynamic Brush Pointer Cursor Indicator */}
          {brushPos && (activeTool === "arrow" || activeTool === "crop") && (
            <div
              style={{
                position: "absolute",
                left: `${brushPos.x}px`,
                top: `${brushPos.y}px`,
                width: "28px",
                height: "28px",
                borderRadius: "50%",
                border: "2px dashed #38bdf8",
                boxShadow: "0 0 8px rgba(56, 189, 248, 0.6)",
                transform: "translate(-50%, -50%)",
                pointerEvents: "none",
                zIndex: 45
              }}
            />
          )}

          {/* SVG Overlay for Arrow Layers with Opacity Control */}
          <svg className="image-canvas-svg-overlay" aria-hidden="true" style={{ opacity: maskOpacity }}>
            <defs>
              <marker id="arrowhead-red" markerWidth="10" markerHeight="7" refX="9" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#ef4444" />
              </marker>
              <marker id="arrowhead-yellow" markerWidth="10" markerHeight="7" refX="9" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#eab308" />
              </marker>
              <marker id="arrowhead-blue" markerWidth="10" markerHeight="7" refX="9" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#3b82f6" />
              </marker>
              <marker id="arrowhead-white" markerWidth="10" markerHeight="7" refX="9" refY="3.5" orient="auto">
                <polygon points="0 0, 10 3.5, 0 7" fill="#ffffff" />
              </marker>
            </defs>

            {editState.arrowLayers.map((arrow: ArrowLayer) => {
              const markerId =
                arrow.color === "#ef4444"
                  ? "url(#arrowhead-red)"
                  : arrow.color === "#eab308"
                  ? "url(#arrowhead-yellow)"
                  : arrow.color === "#3b82f6"
                  ? "url(#arrowhead-blue)"
                  : "url(#arrowhead-white)";

              const isSelected = selectedArrowId === arrow.id;

              return (
                <g key={arrow.id} onClick={() => onArrowClick?.(arrow.id)}>
                  <line
                    x1={`${arrow.x1}%`}
                    y1={`${arrow.y1}%`}
                    x2={`${arrow.x2}%`}
                    y2={`${arrow.y2}%`}
                    stroke={isSelected ? "#38bdf8" : arrow.color}
                    strokeWidth={arrow.strokeWidth + (isSelected ? 3 : 0)}
                    markerEnd={markerId}
                    strokeLinecap="round"
                    style={{ cursor: "pointer", filter: isSelected ? "drop-shadow(0 0 4px #0284c7)" : "none" }}
                  />
                </g>
              );
            })}
          </svg>

          {/* Crop Box with Interactive Resize Handles */}
          {editState.crop && (
            <div
              className="image-crop-box"
              style={{
                left: `${Math.max(0, Math.min(100, editState.crop.x))}%`,
                top: `${Math.max(0, Math.min(100, editState.crop.y))}%`,
                width: `${Math.max(0, Math.min(100 - editState.crop.x, editState.crop.width))}%`,
                height: `${Math.max(0, Math.min(100 - editState.crop.y, editState.crop.height))}%`,
                opacity: maskOpacity
              }}
              aria-hidden="true"
            >
              <span className="crop-handle crop-handle--nw" />
              <span className="crop-handle crop-handle--ne" />
              <span className="crop-handle crop-handle--sw" />
              <span className="crop-handle crop-handle--se" />
            </div>
          )}

          {/* Text Overlay Layers */}
          {editState.textLayers.map((layer) => {
            const isSelected = layer.id === selectedTextId;
            return (
              <button
                className={isSelected ? "image-text-layer image-text-layer--selected" : "image-text-layer"}
                type="button"
                style={{
                  left: `${Math.max(0, Math.min(95, layer.x))}%`,
                  top: `${Math.max(0, Math.min(95, layer.y))}%`,
                  color: layer.color,
                  fontSize: `${Math.max(12, layer.fontSize / 8)}px`,
                  opacity: maskOpacity
                }}
                onPointerDown={(event) => {
                  event.stopPropagation();
                  try {
                    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
                  } catch {
                    // pointer capture unsupported or blocked
                  }
                  onTextPointerDown(event, layer);
                }}
                onPointerUp={(event) => {
                  try {
                    if ((event.currentTarget as HTMLElement).hasPointerCapture(event.pointerId)) {
                      (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
                    }
                  } catch {
                    // ignore
                  }
                }}
                key={layer.id}
                title="Fai clic e trascina per spostare il testo sul canvas, usa le frecce della tastiera (con Shift per spostamenti ampi) o modifica nel pannello testo"
              >
                {layer.text}
              </button>
            );
          })}
        </div>
      ) : (
        <div className="generated-image-preview generated-image-preview--empty" role="status">
          Caricamento anteprima...
        </div>
      )}
    </div>
  );
}
