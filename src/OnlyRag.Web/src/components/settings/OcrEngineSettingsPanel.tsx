import {
  OcrFieldLabel,
  OcrRangeField,
  formatOcrDecimal,
  getOcrSelectOptions
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

const PADDLE_OCR_MODEL_PRESETS = ["PP-OCRv5"];
const PADDLE_OCR_MODEL_VERSIONS = ["PP-OCRv5"];

export function OcrEngineSettingsPanel() {
  const {
    ocrFormState,
    diagnostics,
    applyOcrProfile,
    updateOcrSettings,
    saveOcrSettings,
    isBusy,
    hasDirtyOcrSettings
  } = useSettingsSectionContext();
  const gpuCapability = diagnostics?.ocrGpuCapability ?? null;
  const isGpuUsable = Boolean(gpuCapability?.isUsable);
  const gpuBlockReason = gpuCapability?.blockReason ?? "Verifica diagnostica OCR GPU non ancora disponibile.";

  return (
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>OCR PaddleOCR</h3>
            <span className="status-chip status-chip--muted">{ocrFormState.profile}</span>
          </div>
          <div className="settings-form">
            <div className="settings-grid">
              <label className="field-group" htmlFor="ocr-profile">
                <OcrFieldLabel
                  text="Profilo"
                  tooltip="Profilo generale del bridge OCR. Veloce riduce costo, accurato privilegia qualita e controlli piu conservativi."
                />
                <select
                  id="ocr-profile"
                  value={ocrFormState.profile}
                  onChange={(event) => applyOcrProfile(event.target.value)}
                >
                  <option value="fast">Veloce</option>
                  <option value="balanced">Bilanciato</option>
                  <option value="accurate">Accurato</option>
                  <option value="custom">Personalizzato</option>
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-device">
                <OcrFieldLabel
                  text="Dispositivo"
                  tooltip="CPU e' piu compatibile. GPU usa il runtime NVIDIA preparato da Configura OCR quando disponibile."
                />
                <select
                  id="ocr-device"
                  value={ocrFormState.device}
                  onChange={(event) => updateOcrSettings({ device: event.target.value })}
                >
                  <option value="cpu">CPU</option>
                  <option value="gpu" disabled={!isGpuUsable}>GPU</option>
                </select>
              </label>
              <OcrRangeField
                id="ocr-pdf-dpi"
                label="DPI PDF"
                tooltip="Risoluzione usata per convertire pagine PDF in immagini prima dell'OCR. Valori bassi sono piu veloci, valori alti leggono meglio testi piccoli."
                min={96}
                max={400}
                value={ocrFormState.pdfDpi}
                onChange={(value) => updateOcrSettings({ pdfDpi: value })}
              />
              <label className="field-group" htmlFor="ocr-model-preset">
                <OcrFieldLabel
                  text="Preset modello"
                  tooltip="Preset PaddleOCR passato al bridge. Il menu mostra i preset noti nel progetto e conserva eventuali valori gia salvati."
                />
                <select
                  id="ocr-model-preset"
                  value={ocrFormState.modelPreset}
                  onChange={(event) => updateOcrSettings({ modelPreset: event.target.value })}
                >
                  {getOcrSelectOptions(ocrFormState.modelPreset, PADDLE_OCR_MODEL_PRESETS).map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-model-version">
                <OcrFieldLabel
                  text="Versione modello"
                  tooltip="Versione OCR passata a PaddleOCR come ocr_version quando supportata. Il valore salvato resta selezionabile anche se non e' nell'elenco noto."
                />
                <select
                  id="ocr-model-version"
                  value={ocrFormState.modelVersion}
                  onChange={(event) => updateOcrSettings({ modelVersion: event.target.value })}
                >
                  {getOcrSelectOptions(ocrFormState.modelVersion, PADDLE_OCR_MODEL_VERSIONS).map((option) => (
                    <option key={option} value={option}>{option}</option>
                  ))}
                </select>
              </label>
              <OcrRangeField
                id="ocr-detection-side-limit"
                label="Lato massimo detection"
                tooltip="Dimensione massima usata dal detector testo. Valori bassi riducono tempo e memoria, valori alti aiutano pagine grandi o dettagli fini."
                min={320}
                max={4096}
                value={ocrFormState.detectionSideLimit}
                onChange={(value) => updateOcrSettings({ detectionSideLimit: value })}
              />
              <OcrRangeField
                id="ocr-detection-threshold"
                label="Soglia detection"
                tooltip="Confidenza minima per proporre aree di testo. Valori bassi rilevano piu elementi, valori alti scartano rumore."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.detectionThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionThreshold: value })}
              />
              <OcrRangeField
                id="ocr-detection-box-threshold"
                label="Soglia box"
                tooltip="Filtro sui riquadri rilevati. Valori bassi sono piu permissivi, valori alti tengono solo box piu affidabili."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.detectionBoxThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionBoxThreshold: value })}
              />
              <OcrRangeField
                id="ocr-detection-unclip-ratio"
                label="Unclip ratio"
                tooltip="Espansione dei box di testo rilevati. Valori bassi sono piu stretti, valori alti includono piu margine intorno al testo."
                min={1}
                max={3}
                step={0.05}
                value={ocrFormState.detectionUnclipRatio}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ detectionUnclipRatio: value })}
              />
              <OcrRangeField
                id="ocr-recognition-score-threshold"
                label="Soglia riconoscimento"
                tooltip="Confidenza minima delle parole riconosciute. Valori bassi mantengono piu testo, valori alti privilegiano risultati piu affidabili."
                min={0.01}
                max={0.99}
                step={0.01}
                value={ocrFormState.recognitionScoreThreshold}
                formatValue={formatOcrDecimal}
                onChange={(value) => updateOcrSettings({ recognitionScoreThreshold: value })}
              />
              <OcrRangeField
                id="ocr-recognition-batch-size"
                label="Batch riconoscimento"
                tooltip="Numero di crop di testo riconosciuti insieme. Valori bassi consumano meno memoria, valori alti possono accelerare su hardware adeguato."
                min={1}
                max={32}
                value={ocrFormState.recognitionBatchSize}
                onChange={(value) => updateOcrSettings({ recognitionBatchSize: value })}
              />
              <OcrRangeField
                id="ocr-cpu-threads"
                label="Thread CPU"
                tooltip="Thread CPU dedicati a PaddleOCR. Valori bassi lasciano il PC piu reattivo, valori alti possono ridurre i tempi OCR."
                min={1}
                max={16}
                value={ocrFormState.cpuThreads}
                onChange={(value) => updateOcrSettings({ cpuThreads: value })}
              />
            </div>
            <label className="toggle-row" htmlFor="ocr-textline-orientation">
              <input
                id="ocr-textline-orientation"
                type="checkbox"
                checked={ocrFormState.useTextlineOrientation}
                onChange={(event) =>
                  updateOcrSettings({ useTextlineOrientation: event.target.checked })
                }
              />
              <span>Orientamento righe testo</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-orientation">
              <input
                id="ocr-document-orientation"
                type="checkbox"
                checked={ocrFormState.useDocumentOrientationClassification}
                onChange={(event) =>
                  updateOcrSettings({ useDocumentOrientationClassification: event.target.checked })
                }
              />
              <span>Classificazione orientamento documento</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-unwarping">
              <input
                id="ocr-document-unwarping"
                type="checkbox"
                checked={ocrFormState.useDocumentUnwarping}
                onChange={(event) =>
                  updateOcrSettings({ useDocumentUnwarping: event.target.checked })
                }
              />
              <span>Correzione deformazione documento</span>
            </label>
            {!isGpuUsable && (
              <div className="panel-note panel-note--warning" role="status">
                <p>GPU OCR non selezionabile: {gpuBlockReason}</p>
              </div>
            )}
            {isGpuUsable && (
              <div className="panel-note" role="status">
                <p>{gpuCapability?.status}: {gpuCapability?.runtimeDetail}</p>
              </div>
            )}
            <div className="settings-actions">
              <button type="button" onClick={saveOcrSettings} disabled={isBusy}>
                Salva OCR
              </button>
              {hasDirtyOcrSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>
  );
}

