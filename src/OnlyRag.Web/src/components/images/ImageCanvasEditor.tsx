import type { PointerEvent, RefObject } from "react";
import type { GeneratedImage } from "../../api";
import type { EditTool, ImageEditState, TextLayer } from "./imageTypes";

type Props = {
  selectedImage: GeneratedImage | null;
  objectUrl: string | null;
  editState: ImageEditState;
  activeTool: EditTool;
  selectedTextId: number | null;
  previewRef: RefObject<HTMLDivElement | null>;
  onPreviewPointerDown: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerMove: (event: PointerEvent<HTMLDivElement>) => void;
  onPreviewPointerUp: (event: PointerEvent<HTMLDivElement>) => void;
  onTextPointerDown: (event: PointerEvent<HTMLButtonElement>, layer: TextLayer) => void;
};

export function ImageCanvasEditor({
  selectedImage,
  objectUrl,
  editState,
  activeTool,
  selectedTextId,
  previewRef,
  onPreviewPointerDown,
  onPreviewPointerMove,
  onPreviewPointerUp,
  onTextPointerDown
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
      <img className="generated-image-preview" src={objectUrl} alt={selectedImage.prompt} />
      {editState.crop && (
        <span
          className="image-crop-box"
          style={{
            left: `${Math.max(0, Math.min(100, editState.crop.x))}%`,
            top: `${Math.max(0, Math.min(100, editState.crop.y))}%`,
            width: `${Math.max(0, Math.min(100 - editState.crop.x, editState.crop.width))}%`,
            height: `${Math.max(0, Math.min(100 - editState.crop.y, editState.crop.height))}%`
          }}
          aria-hidden="true"
        />
      )}
      {editState.textLayers.map((layer) => (
        <button
          className={layer.id === selectedTextId ? "image-text-layer image-text-layer--selected" : "image-text-layer"}
          type="button"
          style={{
            left: `${Math.max(5, Math.min(95, layer.x))}%`,
            top: `${Math.max(5, Math.min(95, layer.y))}%`,
            color: layer.color,
            fontSize: `${Math.max(12, layer.fontSize / 8)}px`
          }}
          onPointerDown={(event) => onTextPointerDown(event, layer)}
          key={layer.id}
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
