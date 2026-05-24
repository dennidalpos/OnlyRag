import {
  OcrRangeField,
  OcrSelectField,
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
    diagnosticsStatus,
    applyOcrProfile,
    updateOcrSettings,
    saveOcrSettings,
    isBusy,
    hasDirtyOcrSettings
  } = useSettingsSectionContext();
  const gpuCapability = diagnostics?.ocrGpuCapability ?? null;
  const isGpuUsable = Boolean(gpuCapability?.isUsable);
  const isDiagnosticsLoading = diagnosticsStatus === "loading";
  const gpuBlockReason = gpuCapability?.blockReason ?? "GPU OCR non disponibile.";

  return (
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>OCR PaddleOCR</h3>
            <span className="status-chip status-chip--muted">{ocrFormState.profile}</span>
          </div>
          <div className="settings-form">
            <div className="settings-grid">
              <OcrSelectField
                id="ocr-profile"
                label="Profilo"
                tooltip="Profilo generale del bridge OCR. Veloce riduce costo, accurato privilegia qualita e controlli piu conservativi."
                value={ocrFormState.profile}
                options={[
                  { value: "fast", label: "Veloce" },
                  { value: "balanced", label: "Bilanciato" },
                  { value: "accurate", label: "Accurato" },
                  { value: "custom", label: "Personalizzato" }
                ]}
                onChange={applyOcrProfile}
              />
              <OcrSelectField
                id="ocr-device"
                label="Dispositivo"
                tooltip="CPU e' piu compatibile. GPU usa il runtime NVIDIA preparato da Configura OCR quando disponibile."
                value={ocrFormState.device}
                options={[
                  { value: "cpu", label: "CPU" },
                  { value: "gpu", label: "GPU", disabled: !isGpuUsable }
                ]}
                onChange={(value) => updateOcrSettings({ device: value })}
              />
              <OcrRangeField
                id="ocr-pdf-dpi"
                label="DPI PDF"
                tooltip="Risoluzione usata per convertire pagine PDF in immagini prima dell'OCR. Valori bassi sono piu veloci, valori alti leggono meglio testi piccoli."
                min={96}
                max={400}
                value={ocrFormState.pdfDpi}
                onChange={(value) => updateOcrSettings({ pdfDpi: value })}
              />
              <OcrSelectField
                id="ocr-model-preset"
                label="Preset modello"
                tooltip="Preset PaddleOCR passato al bridge. Il menu mostra i preset noti nel progetto e conserva eventuali valori gia salvati."
                value={ocrFormState.modelPreset}
                options={getOcrSelectOptions(ocrFormState.modelPreset, PADDLE_OCR_MODEL_PRESETS).map((option) => ({
                  value: option,
                  label: option
                }))}
                onChange={(value) => updateOcrSettings({ modelPreset: value })}
              />
              <OcrSelectField
                id="ocr-model-version"
                label="Versione modello"
                tooltip="Versione OCR passata a PaddleOCR come ocr_version quando supportata. Il valore salvato resta selezionabile anche se non e' nell'elenco noto."
                value={ocrFormState.modelVersion}
                options={getOcrSelectOptions(ocrFormState.modelVersion, PADDLE_OCR_MODEL_VERSIONS).map((option) => ({
                  value: option,
                  label: option
                }))}
                onChange={(value) => updateOcrSettings({ modelVersion: value })}
              />
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
            {isDiagnosticsLoading && (
              <div className="panel-note" role="status">
                <p>Verifica diagnostica OCR GPU in corso.</p>
              </div>
            )}
            {!isDiagnosticsLoading && !isGpuUsable && (
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

