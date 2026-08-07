import { useState } from "react";
import { useSettingsSectionContext } from "./SettingsSectionContext";
import { ProgressBar } from "../common/ProgressBar";
import { LogViewerModal } from "../layout/LogViewerModal";
import { InfoTip } from "../common/InfoTip";

export function DiagnosticsPanel() {
  const [showLogViewer, setShowLogViewer] = useState(false);
  const {
    diagnostics,
    diagnosticsStatus,
    ocrProvisionStatus,
    refreshDiagnostics,
    isBusy,
    openLogsFolder,
    configureOcrRuntime,
    cancelOcrRuntimeConfiguration,
    restoreBalancedDefaults,
    requestAppDataReset
  } = useSettingsSectionContext();
  const canRepairOcrRuntime =
    diagnostics?.ocrGpuCapability.status === "Runtime OCR da riparare" ||
    ocrProvisionStatus?.message?.startsWith("Runtime OCR locale incompleto o danneggiato.") === true;
  const ocrConfigureButtonLabel = ocrProvisionStatus?.isRunning
    ? "Configurazione OCR..."
    : canRepairOcrRuntime
      ? "Ripara OCR"
      : diagnostics?.ocrIsConfigured
        ? "Configura OCR"
        : diagnostics?.ocrGpuCapability.nvidiaName
          ? "Installa OCR GPU"
          : "Installa OCR CPU";
  const detectedGpuName =
    diagnostics?.ocrGpuCapability.nvidiaName ??
    diagnostics?.systemTelemetry.gpu?.name ??
    null;
  const detectedGpuDriver =
    diagnostics?.ocrGpuCapability.driverVersion ??
    diagnostics?.systemTelemetry.gpu?.driverVersion ??
    null;
  const gpuDetected = Boolean(detectedGpuName || detectedGpuDriver);
  const noNvidiaGpu = diagnostics?.ocrGpuCapability.capabilityStatus === "no_nvidia_gpu";
  const shouldShowOcrProvisionStatus = Boolean(
    ocrProvisionStatus?.isRunning ||
    ocrProvisionStatus?.lastError ||
    (!diagnostics?.ocrIsConfigured && ocrProvisionStatus)
  );

  return (
    <div className="settings-card">
      <div className="settings-card__header">
        <h3>Diagnostica</h3>
        {diagnostics && (
          <span className="status-chip status-chip--muted">v{diagnostics.appVersion}</span>
        )}
      </div>
      <div className="settings-form">
        {diagnostics ? (
          <>
            <div className="diagnostic-row">
              <span className="diagnostic-label">
                Database
                <InfoTip label="Archivio locale">Percorso del database SQLite locale che conserva documenti, metadati e impostazioni.</InfoTip>
              </span>
              <code className="diagnostic-value">{diagnostics.databasePath}</code>
            </div>
            <div className="diagnostic-row">
              <span className="diagnostic-label">Cartella log</span>
              <code className="diagnostic-value">{diagnostics.logsDirectory}</code>
            </div>
            <div className="diagnostic-row">
              <span className="diagnostic-label">Ollama</span>
              <span
                className={`status-chip status-chip--${diagnostics.ollamaIsReachable ? "online" : "offline"}`}
              >
                {diagnostics.ollamaStatus}
              </span>
              {diagnostics.ollamaVersion && (
                <span className="diagnostic-value">v{diagnostics.ollamaVersion}</span>
              )}
            </div>
            {diagnostics.ollamaRunningModels && diagnostics.ollamaRunningModels.length > 0 && (
              <div className="panel-note">
                <p>
                  <strong>Modelli caricati ({diagnostics.ollamaRunningModels.length}):</strong>{" "}
                  {diagnostics.ollamaRunningModels.map((model) => {
                    const info: string[] = [];
                    if (model.contextLength) info.push(`${model.contextLength.toLocaleString("it-IT")} ctx`);
                    if (model.sizeVram) info.push(`${Math.round(model.sizeVram / 1024 / 1024)} MB VRAM`);
                    return info.length > 0 ? `${model.name} (${info.join(", ")})` : model.name;
                  }).join(", ")}
                </p>
              </div>
            )}
            <div className="diagnostic-row">
              <span className="diagnostic-label">
                Qdrant
                <InfoTip label="Indice vettoriale">Servizio locale che conserva i vettori usati per la ricerca semantica.</InfoTip>
              </span>
              <span
                className={`status-chip status-chip--${diagnostics.qdrant.isReachable ? "online" : "offline"}`}
              >
                {diagnostics.qdrant.status}
              </span>
              <code className="diagnostic-value">{diagnostics.qdrant.grpcEndpoint}</code>
            </div>
            {diagnostics.qdrant.warning && (
              <div className="panel-note panel-note--warning">
                <p>{diagnostics.qdrant.warning}</p>
              </div>
            )}
            {diagnostics.qdrant.error && (
              <div className="panel-note panel-note--warning">
                <p>{diagnostics.qdrant.error}</p>
              </div>
            )}
            <div className="diagnostic-row">
              <span className="diagnostic-label">OCR ({diagnostics.ocrEngineName})</span>
              <span
                className={`status-chip status-chip--${diagnostics.ocrIsConfigured ? "online" : "offline"}`}
              >
                {diagnostics.ocrStatus}
              </span>
            </div>
            <div className="diagnostic-row">
              <span className="diagnostic-label">GPU rilevata</span>
              <span
                className={`status-chip status-chip--${gpuDetected ? "online" : "offline"}`}
              >
                {gpuDetected ? "Sì" : "No"}
              </span>
              {gpuDetected && (
                <span className="diagnostic-value">
                  {[detectedGpuName, detectedGpuDriver].filter(Boolean).join(" - ")}
                </span>
              )}
            </div>
            {!noNvidiaGpu && (
              <div className="diagnostic-row">
                <span className="diagnostic-label">
                  Supporto OCR GPU
                  <InfoTip label="Accelerazione OCR">Indica se il runtime OCR può usare una GPU NVIDIA per elaborare più velocemente le immagini.</InfoTip>
                </span>
                <span
                  className={`status-chip status-chip--${diagnostics.ocrGpuCapability.isUsable ? "online" : "offline"}`}
                >
                  {diagnostics.ocrGpuCapability.isUsable ? "Disponibile" : "Non disponibile"}
                </span>
                <span className="diagnostic-value">
                  {diagnostics.ocrGpuCapability.isUsable
                    ? diagnostics.ocrGpuCapability.runtimeDetail ?? diagnostics.ocrGpuCapability.status
                    : diagnostics.ocrGpuCapability.blockReason ?? diagnostics.ocrGpuCapability.status}
                </span>
              </div>
            )}
            {shouldShowOcrProvisionStatus && (
              <div
                className={`panel-note${ocrProvisionStatus?.lastError ? " panel-note--warning" : ""}`}
                role={ocrProvisionStatus?.isRunning ? "status" : "alert"}
              >
                <p>{ocrProvisionStatus?.message ?? "OCR da installare. Installa le dipendenze locali per abilitare OCR."}</p>
                {ocrProvisionStatus?.isRunning && (
                  <ProgressBar
                    label={ocrProvisionStatus.stepLabel ?? "Configurazione OCR in corso"}
                    value={ocrProvisionStatus.progressPercent}
                    indeterminate={!ocrProvisionStatus.progressPercent}
                  />
                )}
                {ocrProvisionStatus?.runtimeDetail && <p>{ocrProvisionStatus.runtimeDetail}</p>}
                {ocrProvisionStatus?.lastError && <p>{ocrProvisionStatus.lastError}</p>}
              </div>
            )}
          </>
        ) : diagnosticsStatus === "loading" ? (
          <div className="panel-note" role="status">
            <p>Lettura diagnostica in corso.</p>
          </div>
        ) : (
          <div className="panel-note">
            <p>Dati diagnostici non disponibili.</p>
          </div>
        )}
        <div className="settings-actions">
          <button
            type="button"
            className="button-secondary"
            onClick={() => void refreshDiagnostics()}
            disabled={isBusy}
            aria-label="Rilegge diagnostica"
          >
            Aggiorna
          </button>
          <button
            type="button"
            className="button-secondary"
            onClick={() => setShowLogViewer(true)}
            aria-label="Apre visualizzatore log"
          >
            🔍 Visualizza Log Live
          </button>
          <button
            type="button"
            onClick={() => void openLogsFolder()}
            disabled={isBusy}
            title="Apre la cartella locale dei log applicativi."
          >
            Apri cartella log
          </button>
          <button
            type="button"
            className="button-secondary"
            onClick={() => void configureOcrRuntime("auto")}
            disabled={isBusy || Boolean(ocrProvisionStatus?.isRunning)}
            title="Installa, configura o ripara il runtime OCR locale."
          >
            {ocrConfigureButtonLabel}
          </button>
          {ocrProvisionStatus?.isRunning && (
            <button
              type="button"
              className="button-danger"
              onClick={() => void cancelOcrRuntimeConfiguration()}
              disabled={isBusy}
              title="Interrompe la configurazione OCR in corso."
            >
              Annulla OCR
            </button>
          )}
        </div>
        <div className="settings-actions">
          <button
            type="button"
            className="button-secondary"
            onClick={() => void restoreBalancedDefaults()}
            disabled={isBusy}
            title="Ripristina solo le impostazioni bilanciate, senza eliminare dati."
          >
            Ripristina default
          </button>
          <button
            type="button"
            className="button-danger"
            onClick={() => void requestAppDataReset()}
            disabled={isBusy}
            title="Richiede conferma e programma l'eliminazione dei dati al prossimo avvio."
          >
            Reset dati al riavvio
          </button>
        </div>
      </div>

      {showLogViewer && (
        <LogViewerModal
          onClose={() => setShowLogViewer(false)}
        />
      )}
    </div>
  );
}
