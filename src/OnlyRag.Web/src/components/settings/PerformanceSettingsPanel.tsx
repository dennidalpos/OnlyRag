import {
  SettingsRangeField
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function PerformanceSettingsPanel() {
  const {
    performanceFormState,
    setPerformanceFormState,
    recommendedMaxContextChunks,
    chatModelDetailsLoading,
    embeddingModelDetailsLoading,
    savePerformanceSettings,
    isBusy
  } = useSettingsSectionContext();

  return (
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Prestazioni</h3>
            {performanceFormState.enableLowResourceMode && (
              <span className="status-chip status-chip--offline">Modalità risparmio risorse</span>
            )}
          </div>
          <div className="settings-form">
            <label className="toggle-row" htmlFor="low-resource-mode">
              <input
                id="low-resource-mode"
                type="checkbox"
                checked={performanceFormState.enableLowResourceMode}
                onChange={(event) =>
                  setPerformanceFormState((current) => ({
                    ...current,
                    enableLowResourceMode: event.target.checked
                  }))
                }
              />
              <span>Modalità PC poco performante</span>
            </label>
            {performanceFormState.enableLowResourceMode && (
              <div className="panel-note" style={{ marginTop: 0 }}>
                <p>Forza job paralleli, batch OCR, embedding e traduzione a 1. Consigliato su macchine con meno di 8 GB di RAM o CPU lenta.</p>
              </div>
            )}
            <div className="settings-grid">
              <SettingsRangeField
                id="max-parallel-jobs"
                label="Job paralleli"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxParallelJobs}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxParallelJobs: value }))
                }
              />
              <SettingsRangeField
                id="ocr-parallel-pages"
                label="Pagine OCR parallele"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxOcrParallelPages}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxOcrParallelPages: value }))
                }
              />
              <SettingsRangeField
                id="performance-embedding-batch"
                label="Batch embedding"
                min={1}
                max={8}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.embeddingBatchSize}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, embeddingBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="translation-batch-size"
                label="Batch traduzione"
                min={1}
                max={4}
                value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.translationBatchSize}
                disabled={performanceFormState.enableLowResourceMode}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, translationBatchSize: value }))
                }
              />
              <SettingsRangeField
                id="max-context-chunks"
                label="Chunk contesto"
                min={1}
                max={24}
                value={performanceFormState.maxContextChunks}
                hint={recommendedMaxContextChunks ? `Suggerito: ${recommendedMaxContextChunks}` : null}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, maxContextChunks: value }))
                }
              />
              <SettingsRangeField
                id="performance-request-timeout"
                label="Timeout richieste"
                min={5}
                max={600}
                value={performanceFormState.requestTimeoutSeconds}
                formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
                onChange={(value) =>
                  setPerformanceFormState((current) => ({ ...current, requestTimeoutSeconds: value }))
                }
              />
            </div>
            {(chatModelDetailsLoading || embeddingModelDetailsLoading) && (
              <div className="panel-note">
                <p>Lettura dettagli modello in corso.</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="button" onClick={savePerformanceSettings} disabled={isBusy}>
                Salva prestazioni
              </button>
            </div>
          </div>
        </div>
  );
}




