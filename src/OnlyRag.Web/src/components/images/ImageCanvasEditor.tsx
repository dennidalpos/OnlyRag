import type { PointerEvent, RefObject } from "react";
import type { GeneratedImage } from "../../api";
import type { ArrowLayer, EditTool, ImageEditState, TextLayer } from "./imageTypes";

type Props = {
  selectedImage: GeneratedImage | null;
  objectUrl: string | null;
  editState: ImageEditState;
  activeTool: EditTool;
  selectedTextId: number | null;
  selectedArrowId: number | null;
  previewRef: RefObject<HTMLDivElement | null>;
  onPreviewPointerDown: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerMove: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerUp: () => void;
  onTextPointerDown: (event: PointerEvent<HTMLButtonElement>, layer: TextLayer) => void;
  onArrowClick?: (id: number) => void;
};

export function ImageCanvasEditor({
  selectedImage,
  objectUrl,
  editState,
  activeTool,
  selectedTextId,
  selectedArrowId,
  previewRef,
  onPreviewPointerDown,
  onPreviewPointerMove,
  onPreviewPointerUp,
  onTextPointerDown,
  onArrowClick
}: Props) {
  if (!selectedImage) {
    return (
      <div className="generated-image-preview generated-image-preview--empty" role="status">
        Nessuna immagine selezionata.
      </div>
    );
  }

  return objectUrl ? (
    <div
      className={`generated-image-preview-frame generated-image-preview-frame--${activeTool}`}
      ref={previewRef}
      onPointerDown={onPreviewPointerDown}
      onPointerMove={onPreviewPointerMove}
      onPointerUp={onPreviewPointerUp}
    >
      <div className="image-preview-header-badge">
        <span className="image-filename">{selectedImage.fileName}</span> ({selectedImage.width}x{selectedImage.height}px)
      </div>
      <img
        className="generated-image-preview"
        src={objectUrl}
        alt={selectedImage.prompt}
        draggable={false}
      />

      {/* SVG Overlay for Arrow Layers */}
      <svg className="image-canvas-svg-overlay" aria-hidden="true">
        <defs>
          <marker
            id="arrowhead-red"
            markerWidth="10"
            markerHeight="7"
            refX="9"
            refY="3.5"
            orient="auto"
          >
            <polygon points="0 0, 10 3.5, 0 7" fill="#ef4444" />
          </marker>
          <marker
            id="arrowhead-yellow"
            markerWidth="10"
            markerHeight="7"
            refX="9"
            refY="3.5"
            orient="auto"
          >
            <polygon points="0 0, 10 3.5, 0 7" fill="#eab308" />
          </marker>
          <marker
            id="arrowhead-blue"
            markerWidth="10"
            markerHeight="7"
            refX="9"
            refY="3.5"
            orient="auto"
          >
            <polygon points="0 0, 10 3.5, 0 7" fill="#3b82f6" />
          </marker>
          <marker
            id="arrowhead-white"
            markerWidth="10"
            markerHeight="7"
            refX="9"
            refY="3.5"
            orient="auto"
          >
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
                stroke={arrow.color}
                strokeWidth={arrow.strokeWidth + (isSelected ? 2 : 0)}
                markerEnd={markerId}
                strokeLinecap="round"
                style={{ cursor: "pointer" }}
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
            height: `${Math.max(0, Math.min(100 - editState.crop.y, editState.crop.height))}%`
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
      {editState.textLayers.map((layer) => (
        <button
          className={layer.id === selectedTextId ? "image-text-layer image-text-layer--selected" : "image-text-layer"}
          type="button"
          style={{
            left: `${Math.max(0, Math.min(95, layer.x))}%`,
            top: `${Math.max(0, Math.min(95, layer.y))}%`,
            color: layer.color,
            fontSize: `${Math.max(12, layer.fontSize / 8)}px`
          }}
          onPointerDown={(event) => {
            event.currentTarget.setPointerCapture(event.pointerId);
            onTextPointerDown(event, layer);
          }}
          onPointerUp={(event) => {
            if (event.currentTarget.hasPointerCapture(event.pointerId)) {
              event.currentTarget.releasePointerCapture(event.pointerId);
            }
            onPreviewPointerUp();
          }}
          key={layer.id}
          title="Fai clic e trascina per spostare, o modifica nel pannello testo"
        >
          {layer.text}
        </button>
      ))}
    </div>
  ) : (
    <div className="generated-image-preview generated-image-preview--empty" role="status">
      Caricamento...
    </div>
  );
}
