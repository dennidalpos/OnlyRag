import { SettingsRangeField } from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OcrRuntimeSettingsPanel() {
  const {
    ocrProcessingFormState,
    setOcrProcessingFormState,
    saveOcrProcessingSettings,
    isBusy,
    hasDirtyOcrProcessingSettings
  } = useSettingsSectionContext();

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>OCR runtime</h3>
          </div>
          <div className="settings-form">
            <SettingsRangeField
              id="ocr-processing-retries"
              label="Retry OCR"
              tooltip="Tentativi aggiuntivi per pagina quando OCR fallisce o produce risultati insufficienti."
              min={0}
              max={2}
              value={ocrProcessingFormState.maxRetries}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, maxRetries: value }))
              }
            />
            <SettingsRangeField
              id="ocr-processing-timeout"
              label="Timeout pagina"
              tooltip="Tempo massimo concesso all'OCR per ogni pagina."
              min={15}
              max={600}
              step={15}
              value={ocrProcessingFormState.pageTimeoutSeconds}
              formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, pageTimeoutSeconds: value }))
              }
            />
            <SettingsRangeField
              id="ocr-processing-low-confidence"
              label="Soglia bassa confidenza"
              tooltip="Soglia sotto cui il testo OCR viene considerato poco affidabile."
              min={0.01}
              max={0.99}
              step={0.01}
              value={ocrProcessingFormState.lowConfidenceThreshold}
              formatValue={(value) => value.toFixed(2)}
              onChange={(value) =>
                setOcrProcessingFormState((current) => ({ ...current, lowConfidenceThreshold: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveOcrProcessingSettings} disabled={isBusy || !hasDirtyOcrProcessingSettings}>
                Salva OCR runtime
              </button>
              {hasDirtyOcrProcessingSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

