import {
  SettingsRangeField,
  UnifiedPresetBar,
  type UnifiedPresetLevel
} from "./SettingsSection.helpers";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function IngestionSettingsPanel() {
  const {
    embeddingRecommendations,
    ingestionFormState,
    setIngestionFormState,
    saveIngestionSettings,
    isBusy,
    hasDirtyIngestionSettings
  } = useSettingsSectionContext();

  const activePreset: UnifiedPresetLevel =
    ingestionFormState.chunkSizeTokens === 400 && ingestionFormState.overlapTokens === 60 ? "basso" :
    ingestionFormState.chunkSizeTokens === 800 && ingestionFormState.overlapTokens === 120 ? "medio" :
    ingestionFormState.chunkSizeTokens === 1600 && ingestionFormState.overlapTokens === 240 ? "alto" :
    "custom";

  function handleSelectPreset(preset: UnifiedPresetLevel) {
    if (preset === "basso") {
      setIngestionFormState({ chunkSizeTokens: 400, overlapTokens: 60 });
    } else if (preset === "medio") {
      setIngestionFormState({ chunkSizeTokens: 800, overlapTokens: 120 });
    } else if (preset === "alto") {
      setIngestionFormState({ chunkSizeTokens: 1600, overlapTokens: 240 });
    }
  }

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Ingestione &amp; RAG</h3>
            {embeddingRecommendations && (
              <span
                className="status-chip status-chip--muted"
                title="Intervallo chunk suggerito in base al contesto del modello embedding."
              >
                {embeddingRecommendations.chunkMinimum.toLocaleString("it-IT")}-
                {embeddingRecommendations.chunkMaximum.toLocaleString("it-IT")}
              </span>
            )}
          </div>
          <div className="settings-form">
            <UnifiedPresetBar
              title="Preset Chunking"
              subtitle="Configura la dimensione dei blocchi di testo per indicizzazione e vector search."
              allowedPresets={["basso", "medio", "alto", "custom"]}
              activePreset={activePreset}
              onSelectPreset={handleSelectPreset}
            />
            <SettingsRangeField
              id="ingestion-chunk-size"
              label="Dimensione chunk"
              tooltip="Dimensione massima dei segmenti indicizzati per ricerca semantica."
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
              tooltip="Token ripetuti tra chunk adiacenti per conservare contesto."
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
              <button type="button" onClick={saveIngestionSettings} disabled={isBusy || !hasDirtyIngestionSettings}>
                Salva ingestione
              </button>
              {hasDirtyIngestionSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

