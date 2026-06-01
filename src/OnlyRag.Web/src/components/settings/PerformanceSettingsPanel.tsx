import type { PerformanceProfile, PerformanceSettings } from "../../api";
import { performanceProfilePresets } from "../SettingsSection.defaults";
import { SettingsFieldLabel, SettingsRangeField } from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

const profileOptions: Array<{ value: PerformanceProfile; label: string; detail: string }> = [
  { value: "auto", label: "Auto", detail: "Selezione automatica da RAM e CPU locali." },
  { value: "eco", label: "Eco", detail: "1 job, batch minimi e timeout piu lungo." },
  { value: "balanced", label: "Bilanciato", detail: "2 job e batch moderati per uso quotidiano." },
  { value: "power", label: "Potente", detail: "4 job e batch ampi: usa piu RAM/VRAM, CPU e carico Ollama." },
  { value: "custom", label: "Personalizzato", detail: "Valori manuali salvati dagli slider." }
];

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
  const currentProfile = profileOptions.find((option) => option.value === performanceFormState.profile)
    ?? profileOptions[0];

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Prestazioni</h3>
            <span className="status-chip status-chip--muted">
              Effettivo: {profileOptions.find((option) => option.value === performanceFormState.effectiveProfile)?.label ?? "Auto"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="performance-profile">
              <SettingsFieldLabel text="Profilo prestazioni" tooltip={currentProfile.detail} />
              <select
                id="performance-profile"
                value={performanceFormState.profile}
                title={currentProfile.detail}
                aria-label="Profilo prestazioni"
                onChange={(event) =>
                  setPerformanceFormState((current) =>
                    applyPerformanceProfile(current, event.target.value as PerformanceProfile)
                  )
                }
              >
                {profileOptions.map((option) => (
                  <option key={option.value} value={option.value}>{option.label}</option>
                ))}
              </select>
            </label>
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




