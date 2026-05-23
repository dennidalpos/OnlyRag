import { useSettingsSectionContext } from "../SettingsSectionContext";

export function DiagnosticsPanel() {
  const {
    diagnostics,
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
    ocrProvisionStatus?.message.startsWith("Runtime OCR locale incompleto o danneggiato.") === true;
  const ocrConfigureButtonLabel = ocrProvisionStatus?.isRunning
    ? "Configurazione OCR..."
    : canRepairOcrRuntime
      ? "Ripara OCR"
      : "Configura OCR";

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
                {ocrConfigureButtonLabel}
              </button>
              {ocrProvisionStatus?.isRunning && (
                <button
                  type="button"
                  className="button-danger"
                  onClick={() => void cancelOcrRuntimeConfiguration()}
                  disabled={isBusy}
                >
                  Annulla OCR
                </button>
              )}
            </div>
            <div className="panel-note panel-note--warning">
              <p>Ripristina default aggiorna solo le impostazioni. Reset dati elimina tutto al prossimo avvio dopo conferma.</p>
            </div>
            <div className="settings-actions">
              <button
                type="button"
                className="button-secondary"
                onClick={() => void restoreBalancedDefaults()}
                disabled={isBusy}
              >
                Ripristina default
              </button>
              <button
                type="button"
                className="button-danger"
                onClick={() => void requestAppDataReset()}
                disabled={isBusy}
              >
                Reset dati al riavvio
              </button>
            </div>
          </div>
        </div>
  );
}

