import {
  SettingsRangeField
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function IngestionSettingsPanel() {
  const {
    embeddingRecommendations,
    ingestionFormState,
    setIngestionFormState,
    saveIngestionSettings,
    isBusy,
    hasDirtyIngestionSettings
  } = useSettingsSectionContext();

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Ingestione</h3>
            {embeddingRecommendations && (
              <span className="status-chip status-chip--muted">
                {embeddingRecommendations.chunkMinimum.toLocaleString("it-IT")}-
                {embeddingRecommendations.chunkMaximum.toLocaleString("it-IT")}
              </span>
            )}
          </div>
          <div className="settings-form">
            <SettingsRangeField
              id="ingestion-chunk-size"
              label="Dimensione chunk"
              min={100}
              max={4000}
              step={50}
              value={ingestionFormState.chunkSizeTokens}
              formatValue={(value) => `${value.toLocaleString("it-IT")} token`}
              hint={embeddingRecommendations ? `Suggerito: ${embeddingRecommendations.chunkMinimum}-${embeddingRecommendations.chunkMaximum}` : null}
              onChange={(value) =>
                setIngestionFormState((current) => {
                  const nextChunkSize = value;
                  return {
                    chunkSizeTokens: nextChunkSize,
                    overlapTokens: Math.min(current.overlapTokens, Math.min(1000, Math.floor(nextChunkSize / 2)))
                  };
                })
              }
            />
            <SettingsRangeField
              id="ingestion-overlap"
              label="Overlap chunk"
              min={0}
              max={Math.min(1000, Math.floor(ingestionFormState.chunkSizeTokens / 2))}
              step={10}
              value={ingestionFormState.overlapTokens}
              formatValue={(value) => `${value.toLocaleString("it-IT")} token`}
              onChange={(value) =>
                setIngestionFormState((current) => ({ ...current, overlapTokens: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveIngestionSettings} disabled={isBusy}>
                Salva ingestione
              </button>
              {hasDirtyIngestionSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

