import { useSettingsSectionContext } from "../SettingsSectionContext";
import {
  formatTelemetryBytes,
  formatTelemetryPercent
} from "../SettingsSection.helpers";

export function DiagnosticsPanel() {
  const {
    diagnostics,
    ocrProvisionStatus,
    refreshDiagnostics,
    isBusy,
    openLogsFolder,
    configureOcrRuntime
  } = useSettingsSectionContext();

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
                  <span className="diagnostic-label">Database</span>
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
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">OCR ({diagnostics.ocrEngineName})</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ocrIsConfigured ? "online" : "offline"}`}
                  >
                    {diagnostics.ocrStatus}
                  </span>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">OCR GPU</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ocrGpuCapability.isUsable ? "online" : "offline"}`}
                  >
                    {diagnostics.ocrGpuCapability.status}
                  </span>
                  {diagnostics.ocrGpuCapability.runtimeDetail && (
                    <span className="diagnostic-value">{diagnostics.ocrGpuCapability.runtimeDetail}</span>
                  )}
                  {diagnostics.ocrGpuCapability.blockReason && (
                    <span className="diagnostic-value">{diagnostics.ocrGpuCapability.blockReason}</span>
                  )}
                </div>
                <div className="telemetry-grid" aria-label="Telemetria sistema locale">
                  <div className="telemetry-item">
                    <span>CPU</span>
                    <strong>{formatTelemetryPercent(diagnostics.systemTelemetry.cpu.usagePercent)}</strong>
                    <small>{diagnostics.systemTelemetry.cpu.logicalProcessorCount} thread logici</small>
                  </div>
                  <div className="telemetry-item">
                    <span>RAM</span>
                    <strong>
                      {formatTelemetryBytes(diagnostics.systemTelemetry.memory.availableBytes)}
                    </strong>
                    <small>
                      liberi di {formatTelemetryBytes(diagnostics.systemTelemetry.memory.totalBytes)}
                    </small>
                  </div>
                  <div className="telemetry-item">
                    <span>Disco {diagnostics.systemTelemetry.systemDisk.name}</span>
                    <strong>
                      {formatTelemetryBytes(diagnostics.systemTelemetry.systemDisk.availableBytes)}
                    </strong>
                    <small>
                      liberi di {formatTelemetryBytes(diagnostics.systemTelemetry.systemDisk.totalBytes)}
                    </small>
                  </div>
                  <div className="telemetry-item">
                    <span>GPU</span>
                    <strong>
                      {diagnostics.systemTelemetry.gpu
                        ? formatTelemetryPercent(diagnostics.systemTelemetry.gpu.usagePercent)
                        : "n/d"}
                    </strong>
                    <small>
                      {diagnostics.systemTelemetry.gpu
                        ? `${diagnostics.systemTelemetry.gpu.name} ${diagnostics.systemTelemetry.gpu.driverVersion}`
                        : "NVIDIA non rilevata"}
                    </small>
                  </div>
                  {diagnostics.systemTelemetry.gpu && (
                    <div className="telemetry-item">
                      <span>VRAM</span>
                      <strong>
                        {formatTelemetryBytes(diagnostics.systemTelemetry.gpu.memoryAvailableBytes)}
                      </strong>
                      <small>
                        liberi di {formatTelemetryBytes(diagnostics.systemTelemetry.gpu.memoryTotalBytes)}
                      </small>
                    </div>
                  )}
                  <div className="telemetry-item">
                    <span>CUDA Paddle</span>
                    <strong>
                      {diagnostics.ocrGpuCapability.compiledWithCuda === null
                        ? "n/d"
                        : diagnostics.ocrGpuCapability.compiledWithCuda ? "Si" : "No"}
                    </strong>
                    <small>
                      {diagnostics.ocrGpuCapability.cudaDeviceCount ?? 0} dispositivi, {diagnostics.ocrGpuCapability.activeDevice ?? "nessuno"}
                    </small>
                  </div>
                </div>
                {!diagnostics.ocrIsConfigured && (
                  <div className="panel-note panel-note--warning" role="alert">
                    <p>{ocrProvisionStatus?.message ?? "OCR non configurato. Configura le dipendenze locali per abilitare OCR."}</p>
                    {ocrProvisionStatus?.runtimeDetail && <p>{ocrProvisionStatus.runtimeDetail}</p>}
                    {ocrProvisionStatus?.lastError && <p>{ocrProvisionStatus.lastError}</p>}
                  </div>
                )}
                {ocrProvisionStatus?.isRunning && (
                  <div className="panel-note" role="status">
                    <p>{ocrProvisionStatus.message}</p>
                    {ocrProvisionStatus.runtimeDetail && <p>{ocrProvisionStatus.runtimeDetail}</p>}
                  </div>
                )}
              </>
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
              >
                Aggiorna
              </button>
              <button type="button" onClick={() => void openLogsFolder()} disabled={isBusy}>
                Apri cartella log
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={() => void configureOcrRuntime()}
                disabled={isBusy || Boolean(ocrProvisionStatus?.isRunning)}
              >
                {ocrProvisionStatus?.isRunning ? "Configurazione OCR..." : "Configura OCR"}
              </button>
            </div>
          </div>
        </div>
  );
}

