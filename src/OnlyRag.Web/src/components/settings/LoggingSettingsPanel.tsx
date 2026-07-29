import { useEffect, useState } from "react";
import { apiRequest } from "../../api";
import type { AppLogLevel, LoggingSettings, LogStorageInfo } from "../../apiTypes/settings";
import { LogViewerModal } from "../LogViewerModal";

export function LoggingSettingsPanel() {
  const [minLevel, setMinLevel] = useState<AppLogLevel>("Trace");
  const [storageInfo, setStorageInfo] = useState<LogStorageInfo | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);
  const [showLogViewer, setShowLogViewer] = useState(false);

  useEffect(() => {
    void loadData();
  }, []);

  async function loadData() {
    setIsLoading(true);
    setStatusMessage(null);
    try {
      const [settings, storage] = await Promise.all([
        apiRequest<LoggingSettings>("/api/settings/logging"),
        apiRequest<LogStorageInfo>("/api/logs/storage")
      ]);
      setMinLevel(settings.minLevel);
      setStorageInfo(storage);
    } catch (err) {
      setStatusMessage(err instanceof Error ? err.message : "Errore nel caricamento delle impostazioni di log.");
    } finally {
      setIsLoading(false);
    }
  }

  async function handleSaveLevel(newLevel: AppLogLevel) {
    setMinLevel(newLevel);
    setIsSaving(true);
    setStatusMessage(null);
    try {
      await apiRequest<LoggingSettings>("/api/settings/logging", {
        method: "POST",
        body: JSON.stringify({ minLevel: newLevel })
      });
      setStatusMessage(newLevel === "None" ? "Log disattivati con successo." : `Livello di log aggiornato a: ${newLevel}`);
    } catch (err) {
      setStatusMessage(err instanceof Error ? err.message : "Errore nel salvataggio del livello di log.");
    } finally {
      setIsSaving(false);
    }
  }

  async function handleClearLogs() {
    if (!window.confirm("Sei sicuro di voler cancellare ed azzerare tutti i log di sistema?")) {
      return;
    }
    setIsSaving(true);
    setStatusMessage(null);
    try {
      await apiRequest<{ success: boolean; message: string }>("/api/logs", {
        method: "DELETE"
      });
      const updatedStorage = await apiRequest<LogStorageInfo>("/api/logs/storage");
      setStorageInfo(updatedStorage);
      setStatusMessage("File di log e buffer in memoria cancellati con successo (0 B).");
    } catch (err) {
      setStatusMessage(err instanceof Error ? err.message : "Errore durante la cancellazione dei log.");
    } finally {
      setIsSaving(false);
    }
  }

  return (
    <div className="settings-card settings-card--wide">
      <div className="settings-card__header">
        <h3>Sistema di Log &amp; Diagnostica Live</h3>
        <button
          type="button"
          className="button-secondary"
          onClick={() => setShowLogViewer(true)}
        >
          🔍 Visualizza Log Live
        </button>
      </div>
      <div className="settings-form">
        {statusMessage && (
          <div className="panel-note" role="status">
            <p>{statusMessage}</p>
          </div>
        )}

        <div className="settings-grid">
          <label className="field-group" htmlFor="logging-min-level">
            <span>Livello Verbosità Log</span>
            <select
              id="logging-min-level"
              value={minLevel}
              disabled={isLoading || isSaving}
              onChange={(e) => void handleSaveLevel(e.target.value as AppLogLevel)}
            >
              <option value="Trace">🔍 Trace - Massimo Verboso (Predefinito)</option>
              <option value="Debug">🐛 Debug - Dettagliato</option>
              <option value="Information">ℹ️ Information - Informativo Generico</option>
              <option value="Warning">⚠️ Warning - Solo Avvisi ed Errori</option>
              <option value="Error">❌ Error - Solo Errori Critici</option>
              <option value="None">🚫 None - Log Disattivati</option>
            </select>
          </label>

          <div className="diagnostic-row">
            <span className="diagnostic-label">Spazio occupato</span>
            <span className="diagnostic-value">
              {storageInfo ? storageInfo.formattedSize : "... KB"}
              {storageInfo && ` (${storageInfo.memoryEntryCount} voci RAM, ${storageInfo.fileCount} file disco)`}
            </span>
          </div>
        </div>

        {storageInfo && (
          <div className="panel-note panel-note--path">
            <p title={storageInfo.logDirectory} aria-label={`Percorso log: ${storageInfo.logDirectory}`}>
              📁 Percorso: {storageInfo.logDirectory}
            </p>
          </div>
        )}

        <div className="settings-actions">
          <button
            type="button"
            className="button-danger"
            onClick={() => void handleClearLogs()}
            disabled={isLoading || isSaving}
          >
            🗑️ Cancella / Azzera Log
          </button>
          <button
            type="button"
            className="button-secondary"
            onClick={() => void loadData()}
            disabled={isLoading}
          >
            🔄 Aggiorna Metadati
          </button>
        </div>
      </div>

      {showLogViewer && (
        <LogViewerModal
          onClose={() => setShowLogViewer(false)}
          onLogsCleared={() => void loadData()}
        />
      )}
    </div>
  );
}
