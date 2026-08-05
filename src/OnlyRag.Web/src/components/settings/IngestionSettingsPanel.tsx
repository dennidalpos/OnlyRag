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
      setIngestionFormState((current) => ({ ...current, chunkSizeTokens: 400, overlapTokens: 60 }));
    } else if (preset === "medio") {
      setIngestionFormState((current) => ({ ...current, chunkSizeTokens: 800, overlapTokens: 120 }));
    } else if (preset === "alto") {
      setIngestionFormState((current) => ({ ...current, chunkSizeTokens: 1600, overlapTokens: 240 }));
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
                    ...current,
                    chunkSizeTokens: nextChunkSize,
                    overlapTokens: Math.min(current.overlapTokens, Math.min(1000, Math.floor(nextChunkSize / 2)))
                  };
                })
              }
            />
            <SettingsRangeField
              id="archive-max-files"
              label="File massimi per archivio"
              tooltip="Limite di protezione contro archivi con un numero eccessivo di file."
              min={1}
              max={100000}
              step={100}
              value={ingestionFormState.archive.maxFileCount}
              formatValue={(value) => value.toLocaleString("it-IT")}
              onChange={(value) => setIngestionFormState((current) => ({
                ...current,
                archive: { ...current.archive, maxFileCount: value }
              }))}
            />
            <SettingsRangeField
              id="archive-max-depth"
              label="Profondita' cartelle archivio"
              tooltip="Profondita' massima dei percorsi estratti; protegge da strutture annidate anomale."
              min={0}
              max={64}
              step={1}
              value={ingestionFormState.archive.maxDirectoryDepth}
              formatValue={(value) => `${value} livelli`}
              onChange={(value) => setIngestionFormState((current) => ({
                ...current,
                archive: { ...current.archive, maxDirectoryDepth: value }
              }))}
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

