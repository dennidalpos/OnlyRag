import { useEffect } from "react";
import {
  OcrRangeField,
  OcrSelectField,
  UnifiedPresetBar,
  formatOcrDecimal,
  type UnifiedPresetLevel
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

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
  const hasNvidiaGpu = gpuCapability?.capabilityStatus !== "no_nvidia_gpu";
  const isDiagnosticsLoading = diagnosticsStatus === "loading";
  const gpuBlockReason = gpuCapability?.blockReason ?? "GPU OCR non disponibile.";

  useEffect(() => {
    if (!hasNvidiaGpu && ocrFormState.device === "gpu") {
      updateOcrSettings({ device: "cpu" });
    }
  }, [hasNvidiaGpu, ocrFormState.device, updateOcrSettings]);

  const activePreset: UnifiedPresetLevel =
    ocrFormState.profile === "auto" ? "auto" :
    ocrFormState.profile === "fast" ? "basso" :
    ocrFormState.profile === "balanced" ? "medio" :
    ocrFormState.profile === "accurate" ? "alto" : "custom";

  function handleSelectPreset(preset: UnifiedPresetLevel) {
    if (preset === "auto") applyOcrProfile("auto");
    else if (preset === "basso") applyOcrProfile("fast");
    else if (preset === "medio") applyOcrProfile("balanced");
    else if (preset === "alto") applyOcrProfile("accurate");
  }

  return (
    <div className="settings-card settings-card--wide">
      <div className="settings-card__header">
        <h3>OCR PaddleOCR</h3>
      </div>
      <div className="settings-form">
        <UnifiedPresetBar
          title="Preset OCR Engine"
          subtitle="Seleziona la precisione dell'estrazione testo e risoluzione DPI per PDF/Immagini."
          allowedPresets={["auto", "basso", "medio", "alto", "custom"]}
          activePreset={activePreset}
          onSelectPreset={handleSelectPreset}
        />
        <div className="settings-grid">
          <OcrSelectField
            id="ocr-device"
            label="Dispositivo"
            tooltip="CPU e' piu compatibile. GPU usa il runtime NVIDIA preparato da Installa OCR quando disponibile."
            value={ocrFormState.device}
            options={[
              { value: "cpu", label: "CPU" },
              ...(hasNvidiaGpu ? [{ value: "gpu", label: "GPU", disabled: !isGpuUsable }] : [])
            ]}
            onChange={(value) =>
              ocrFormState.profile !== "custom"
                ? applyOcrProfile(ocrFormState.profile, value)
                : updateOcrSettings({ device: value })
            }
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
        {!isDiagnosticsLoading && hasNvidiaGpu && !isGpuUsable && (
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
          <button type="button" onClick={saveOcrSettings} disabled={isBusy || !hasDirtyOcrSettings}>
            Salva OCR
          </button>
          {hasDirtyOcrSettings && <span className="dirty-hint">Modifiche non salvate</span>}
        </div>
      </div>
    </div>
  );
}
