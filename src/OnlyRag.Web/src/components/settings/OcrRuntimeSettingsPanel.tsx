import {
  SettingsRangeField,
  getOcrLanguageOptions
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OcrRuntimeSettingsPanel() {
  const {
    ocrProcessingFormState,
    setOcrProcessingFormState,
    ocrLanguages,
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
            <label className="field-group" htmlFor="ocr-processing-language">
              <span>Lingua OCR</span>
              <select
                id="ocr-processing-language"
                value={ocrProcessingFormState.language}
                onChange={(event) =>
                  setOcrProcessingFormState((current) => ({ ...current, language: event.target.value }))
                }
              >
                {getOcrLanguageOptions(ocrProcessingFormState.language, ocrLanguages).map((language) => (
                  <option key={language.code} value={language.code}>
                    {language.label}
                  </option>
                ))}
              </select>
            </label>
            <div className="panel-note">
              <p>Questa lingua viene usata come predefinita per import, reindicizzazione e OCR. La scelta nel dialogo del documento vale solo per quella operazione.</p>
            </div>
            <SettingsRangeField
              id="ocr-processing-retries"
              label="Retry OCR"
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
              <button type="button" onClick={saveOcrProcessingSettings} disabled={isBusy}>
                Salva OCR runtime
              </button>
              {hasDirtyOcrProcessingSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

