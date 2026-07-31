import { useEffect } from "react";
import {
  OcrRangeField,
  OcrSelectField,
  UnifiedPresetBar,
  formatOcrDecimal,
  getOcrSelectOptions,
  type UnifiedPresetLevel
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

