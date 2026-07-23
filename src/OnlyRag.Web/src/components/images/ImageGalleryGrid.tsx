import type { GeneratedImage } from "../../api";
import { formatFileSize } from "../DocumentsSection.formatting";
import { useImageObjectUrl } from "../ImagesSection";

type Props = {
  images: GeneratedImage[];
  selectedImageId: number | null;
  onSelectImage: (id: number) => void;
};

export function ImageGalleryGrid({ images, selectedImageId, onSelectImage }: Props) {
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
      <h4>Galleria altre immagini ({otherImages.length})</h4>
      <div className="generated-images-gallery">
        {otherImages.map((img) => (
          <GalleryCard
            key={img.id}
            img={img}
            isSelected={false}
            onSelectImage={onSelectImage}
          />
        ))}
      </div>
    </div>
  );
}

function GalleryCard({
  img,
  isSelected,
  onSelectImage
}: {
  img: GeneratedImage;
  isSelected: boolean;
  onSelectImage: (id: number) => void;
}) {
  const objectUrl = useImageObjectUrl(img.id);

  return (
    <button
      type="button"
      className={`generated-image-card ${isSelected ? "generated-image-card--selected" : ""}`}
      onClick={() => onSelectImage(img.id)}
      aria-label={`Seleziona ${img.fileName}`}
      title={img.prompt}
    >
      {objectUrl ? (
        <img className="generated-image-card__thumbnail" src={objectUrl} alt={img.prompt} loading="lazy" />
      ) : (
        <div className="generated-image-card__placeholder">🖼️</div>
      )}
      <span className="generated-image-card__body">
        <strong>{img.fileName}</strong>
        <small>{formatFileSize(img.fileSizeBytes)}</small>
      </span>
    </button>
  );
}
