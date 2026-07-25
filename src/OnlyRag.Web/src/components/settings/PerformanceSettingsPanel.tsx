import type { PerformanceProfile, PerformanceSettings } from "../../api";
import { performanceProfilePresets } from "../SettingsSection.defaults";
import { SettingsRangeField, UnifiedPresetBar, type UnifiedPresetLevel } from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";



function applyPerformanceProfile(
  current: PerformanceSettings,
  profile: PerformanceProfile
): PerformanceSettings {
  if (profile === "custom") {
    return {
      ...current,
      profile,
      effectiveProfile: "custom",
      enableLowResourceMode: false
    };
  }

  if (profile === "auto") {
    return {
      ...current,
      profile,
      enableLowResourceMode: false
    };
  }

  return {
    ...performanceProfilePresets[profile],
    profile,
    effectiveProfile: profile
  };
}

export function PerformanceSettingsPanel() {
  const {
    performanceFormState,
    setPerformanceFormState,
    recommendedMaxContextChunks,
    chatModelDetailsLoading,
    embeddingModelDetailsLoading,
    hasDirtyPerformanceSettings,
    savePerformanceSettings,
    isBusy
  } = useSettingsSectionContext();

  const manualControlsEnabled = performanceFormState.profile === "custom";

  const activePreset: UnifiedPresetLevel =
    performanceFormState.profile === "eco" ? "basso" :
    performanceFormState.profile === "balanced" ? "medio" :
    performanceFormState.profile === "power" ? "alto" : "custom";

  function handleSelectPreset(preset: UnifiedPresetLevel) {
    let targetProfile: PerformanceProfile = "custom";
    if (preset === "basso") targetProfile = "eco";
    else if (preset === "medio") targetProfile = "balanced";
    else if (preset === "alto") targetProfile = "power";

    setPerformanceFormState((current) => applyPerformanceProfile(current, targetProfile));
  }

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Prestazioni &amp; Hardware</h3>
          </div>
          <div className="settings-form">
            <UnifiedPresetBar
              title="Preset Prestazioni"
              subtitle="Standardizza l'uso di risorse CPU/RAM/VRAM per tutti gli elaboratori locali."
              allowedPresets={["basso", "medio", "alto", "custom"]}
              activePreset={activePreset}
              onSelectPreset={handleSelectPreset}
            />
            <div className="settings-grid">
              <SettingsRangeField
                id="max-parallel-jobs"
                label="Job paralleli"
                tooltip="Numero massimo di operazioni locali eseguite contemporaneamente."
                min={1}
                max={4}
                value={performanceFormState.maxParallelJobs}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxParallelJobs: value }))
                }
              />
              <SettingsRangeField
                id="ocr-parallel-pages"
                label="Pagine OCR parallele"
                tooltip="Numero di pagine elaborate in parallelo durante OCR."
                min={1}
                max={4}
                value={performanceFormState.maxOcrParallelPages}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxOcrParallelPages: value }))
                }
              />
              <SettingsRangeField
                id="performance-embedding-batch"
                label="Batch embedding"
                tooltip="Numero di chunk inviati insieme al modello embedding."
                min={1}
                max={8}
                value={performanceFormState.embeddingBatchSize}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, embeddingBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="translation-batch-size"
                label="Batch traduzione"
                tooltip="Numero di unita tradotte insieme per ogni richiesta."
                min={1}
                max={4}
                value={performanceFormState.translationBatchSize}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, translationBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="max-context-chunks"
                label="Chunk contesto"
                tooltip="Limita quanti chunk recuperati entrano nella risposta RAG; num_ctx Ollama resta configurato nei modelli."
                min={1}
                max={24}
                value={performanceFormState.maxContextChunks}
                hint={recommendedMaxContextChunks ? `RAG app, suggerito: ${recommendedMaxContextChunks}` : "RAG app"}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxContextChunks: value }))
                }
              />
              <SettingsRangeField
                id="performance-request-timeout"
                label="Timeout richieste"
                tooltip="Tempo massimo concesso alle richieste locali prima dell'errore."
                min={5}
                max={600}
                value={performanceFormState.requestTimeoutSeconds}
                formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
                disabled={!manualControlsEnabled}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, requestTimeoutSeconds: value }))
                }
              />
            </div>
            {(performanceFormState.profile === "power" || performanceFormState.profile === "custom") && (
              <div className="panel-note panel-note--warning">
                <p>
                  Impostazioni avanzate: parallelismo, batch e timeout possono saturare RAM/VRAM o mantenere modelli caricati piu a lungo. Per problemi di contesto o offload verifica con ollama ps.
                </p>
              </div>
            )}
            {(chatModelDetailsLoading || embeddingModelDetailsLoading) && (
              <div className="panel-note">
                <p>Lettura dettagli modello in corso.</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="button" onClick={savePerformanceSettings} disabled={isBusy || !hasDirtyPerformanceSettings}>
                Salva prestazioni
              </button>
              {hasDirtyPerformanceSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}




