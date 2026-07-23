import type { GeneratedImage } from "../../api";
import { formatFileSize } from "../DocumentsSection.formatting";

type Props = {
  images: GeneratedImage[];
  selectedImageId: number | null;
  onSelectImage: (id: number) => void;
};

export function ImageGalleryGrid({ images, selectedImageId, onSelectImage }: Props) {
  if (images.length === 0) {
    return (
      <div className="generated-images-gallery generated-images-gallery--empty" role="status">
        Nessuna immagine generata.
      </div>
    );
  }

  return (
    <div className="generated-images-gallery">
      {images.map((img) => (
        <button
          key={img.id}
          type="button"
          className={`generated-image-card ${selectedImageId === img.id ? "generated-image-card--selected" : ""}`}
          onClick={() => onSelectImage(img.id)}
          aria-label={`Seleziona ${img.fileName}`}
        >
          <span className="generated-image-card__body">
            <strong>{img.fileName}</strong>
            <small>{formatFileSize(img.fileSizeBytes)}</small>
          </span>
        </button>
      ))}
    </div>
  );
}
