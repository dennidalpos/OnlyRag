import type { RefObject } from "react";
import type { ImageModelCatalogEntry } from "../../api";

type ImageConsentDialogProps = {
  pendingConsentModelId: string | null;
  consentModel: ImageModelCatalogEntry | null;
  consentModalRef: RefObject<HTMLDivElement | null>;
  onConfirm: (modelId: string) => void;
  onCancel: () => void;
};

export function ImageConsentDialog({
  pendingConsentModelId,
  consentModel,
  consentModalRef,
  onConfirm,
  onCancel
}: ImageConsentDialogProps) {
  if (!pendingConsentModelId || !consentModel) return null;

  return (
    <div className="modal-overlay" role="dialog" aria-modal="true" aria-labelledby="consent-dialog-title">
      <div className="modal-content" ref={consentModalRef}>
        <div className="modal-header">
          <h3 id="consent-dialog-title">Conferma download modello</h3>
          <button type="button" className="button-icon" onClick={onCancel} title="Chiudi" aria-label="Chiudi finestra">
            ✕
          </button>
        </div>
        <div className="modal-body">
          <p>Stai per scaricare il modello <strong>{consentModel.displayName}</strong>.</p>
          <p>Licenza: <em>{consentModel.licenseLabel}</em></p>
        </div>
        <div className="modal-footer">
          <button type="button" className="button-primary" onClick={() => onConfirm(pendingConsentModelId)}>
            Conferma e scarica
          </button>
          <button type="button" className="button-secondary" onClick={onCancel}>
            Annulla
          </button>
        </div>
      </div>
    </div>
  );
}
