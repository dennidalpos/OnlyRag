import type { MouseEvent } from "react";
import type { GeneratedImage } from "../../api";
import { formatFileSize } from "../DocumentsSection.formatting";
import { useImageObjectUrl } from "../ImagesSection";

type Props = {
  images: GeneratedImage[];
  selectedImageId: number | null;
  onSelectImage: (id: number) => void;
  onDownloadImage?: (img: GeneratedImage) => void;
  onDeleteImage?: (img: GeneratedImage) => void;
  onCopyPrompt?: (prompt: string) => void;
};

export function ImageGalleryGrid({
  images,
  selectedImageId,
  onSelectImage,
  onDownloadImage,
  onDeleteImage,
  onCopyPrompt
}: Props) {
  const otherImages = images.filter((img) => img.id !== selectedImageId);

  if (images.length === 0) {
    return (
      <div className="generated-images-gallery generated-images-gallery--empty" role="status">
        Nessuna immagine generata.
      </div>
    );
  }

  if (otherImages.length === 0) {
    return null;
  }

  return (
    <div className="generated-images-gallery-section">
      <div className="gallery-header-row" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <h4>Galleria altre immagini ({otherImages.length})</h4>
        <small className="gallery-counter">
          Selezionata: {images.findIndex((img) => img.id === selectedImageId) + 1} di {images.length}
        </small>
      </div>
      <div className="generated-images-gallery">
        {otherImages.map((img) => {
          const originalIndex = images.findIndex((i) => i.id === img.id) + 1;
          return (
            <GalleryCard
              key={img.id}
              index={originalIndex}
              img={img}
              isSelected={false}
              onSelectImage={onSelectImage}
              onDownloadImage={onDownloadImage}
              onDeleteImage={onDeleteImage}
              onCopyPrompt={onCopyPrompt}
            />
          );
        })}
      </div>
    </div>
  );
}

function GalleryCard({
  index,
  img,
  isSelected,
  onSelectImage,
  onDownloadImage,
  onDeleteImage,
  onCopyPrompt
}: {
  index: number;
  img: GeneratedImage;
  isSelected: boolean;
  onSelectImage: (id: number) => void;
  onDownloadImage?: (img: GeneratedImage) => void;
  onDeleteImage?: (img: GeneratedImage) => void;
  onCopyPrompt?: (prompt: string) => void;
}) {
  const objectUrl = useImageObjectUrl(img.id);

  function handleActionClick(event: MouseEvent, action: () => void) {
    event.stopPropagation();
    action();
  }

  return (
    <div
      className={`generated-image-card ${isSelected ? "generated-image-card--selected" : ""}`}
      onClick={() => onSelectImage(img.id)}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onSelectImage(img.id);
        }
      }}
      aria-label={`Seleziona immagine ${index}: ${img.fileName}`}
      title={`#${index} · ${img.prompt}`}
    >
      <div className="generated-image-card__badge-row">
        <span className="card-index-badge">#{index}</span>
        <span className="card-resolution-badge">{img.width}x{img.height}</span>
      </div>

      {objectUrl ? (
        <img className="generated-image-card__thumbnail" src={objectUrl} alt={img.prompt} loading="lazy" />
      ) : (
        <div className="generated-image-card__placeholder">🖼️</div>
      )}

      <div className="generated-image-card__body">
        <strong className="card-filename" title={img.fileName}>{img.fileName}</strong>
        <small>{formatFileSize(img.fileSizeBytes)}</small>
      </div>

      <div className="generated-image-card__actions" style={{ display: "flex", gap: "4px", marginTop: "4px" }}>
        {onCopyPrompt && (
          <button
            type="button"
            className="button-secondary button-secondary--xs"
            onClick={(e) => handleActionClick(e, () => onCopyPrompt(img.prompt))}
            title="Copia prompt negli appunti"
          >
            📋 Copia
          </button>
        )}
        {onDownloadImage && (
          <button
            type="button"
            className="button-secondary button-secondary--xs"
            onClick={(e) => handleActionClick(e, () => onDownloadImage(img))}
            title="Scarica immagine locale"
          >
            💾 Scarica
          </button>
        )}
        {onDeleteImage && (
          <button
            type="button"
            className="button-danger button-danger--xs"
            onClick={(e) => handleActionClick(e, () => onDeleteImage(img))}
            title="Elimina immagine"
          >
            🗑️
          </button>
        )}
      </div>
    </div>
  );
}
