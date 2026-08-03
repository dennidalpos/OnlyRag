import { ProgressBar } from "../common/ProgressBar";
import { formatModelSize } from "./SettingsSection.helpers";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function RerankerModelPanel() {
  const {
    rerankerModelInfo,
    downloadRerankerModel,
    cancelRerankerDownload,
    deleteRerankerModel,
    isBusy
  } = useSettingsSectionContext();

  const isDownloaded = rerankerModelInfo?.isDownloaded ?? false;
  const isDownloading = rerankerModelInfo?.isDownloading ?? false;
  const downloadProgress = rerankerModelInfo?.downloadProgress ?? 0;
  const progressPercent = Math.round(downloadProgress * 100);
  const fileSizeText = rerankerModelInfo?.fileSizeBytes
    ? formatModelSize(rerankerModelInfo.fileSizeBytes)
    : "~560 MB";

  return (
    <div className="settings-card" id="reranker-model-panel">
      <div className="settings-card__header">
        <h3>Modello ONNX Cross-Encoder (Re-Ranker)</h3>
      </div>
      <div className="settings-form">
        <p className="settings-card__description">
          Modello neurale di secondo stadio per la ri-classificazione ad alta precisione dei risultati RAG (vettoriale + keyword).
        </p>

        <div className="model-row">
          <div className="model-row__details">
            <strong>{rerankerModelInfo?.name ?? "BGE Re-Ranker Base (ONNX)"}</strong>
            <span>
              {rerankerModelInfo?.modelFileName ?? "bge-reranker-base.onnx"} &bull; {fileSizeText}
            </span>
            <div className="model-status-badge-container" style={{ marginTop: "0.25rem" }}>
              {isDownloaded ? (
                <span className="status-badge status-badge--success">Installato</span>
              ) : isDownloading ? (
                <span className="status-badge status-badge--info">In download ({progressPercent}%)</span>
              ) : (
                <span className="status-badge status-badge--warning">Non installato (Fallback Euristico)</span>
              )}
            </div>

            {isDownloading && (
              <div style={{ marginTop: "0.5rem" }}>
                <ProgressBar
                  label={`Download Re-Ranker ${progressPercent}%`}
                  value={progressPercent}
                />
              </div>
            )}

            {rerankerModelInfo?.downloadError && (
              <div className="job-error-message" style={{ marginTop: "0.5rem" }}>
                Errore download: {rerankerModelInfo.downloadError}
              </div>
            )}
          </div>

          <div className="model-row__actions">
            {!isDownloaded && !isDownloading && (
              <button
                type="button"
                onClick={() => void downloadRerankerModel()}
                disabled={isBusy}
              >
                Scarica modello
              </button>
            )}

            {isDownloading && (
              <button
                type="button"
                className="button-secondary"
                onClick={() => void cancelRerankerDownload()}
                disabled={isBusy}
              >
                Annulla download
              </button>
            )}

            {isDownloaded && !isDownloading && (
              <button
                type="button"
                className="button-danger"
                onClick={() => void deleteRerankerModel()}
                disabled={isBusy}
              >
                Elimina modello
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
