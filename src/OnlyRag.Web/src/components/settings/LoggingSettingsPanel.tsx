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
    <div style={{ display: "flex", flexDirection: "column", gap: 20 }}>
      <div style={{
        background: "linear-gradient(135deg, #1e1b4b 0%, #0f172a 100%)",
        border: "1px solid #4338ca",
        borderRadius: 12,
        padding: 20,
        boxShadow: "0 4px 20px rgba(0,0,0,0.3)"
      }}>
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 12 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <span style={{ fontSize: "1.5rem" }}>🪵</span>
            <div>
              <h3 style={{ margin: 0, color: "#f8fafc", fontSize: "1.1rem", fontWeight: 600 }}>
                Sistema di Log & Diagnostic Debug
              </h3>
              <p style={{ margin: 0, color: "#94a3b8", fontSize: "0.85rem" }}>
                Configura la verbosità per intercettare gli errori e analizzare la Modalità Agente in dettaglio.
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => setShowLogViewer(true)}
            style={{
              background: "#3b82f6",
              color: "#ffffff",
              border: "none",
              borderRadius: 8,
              padding: "8px 16px",
              fontWeight: 600,
              fontSize: "0.88rem",
              cursor: "pointer",
              display: "flex",
              alignItems: "center",
              gap: 8,
              boxShadow: "0 2px 8px rgba(59,130,246,0.3)"
            }}
          >
            🔍 Visualizza Log Live
          </button>
        </div>

        {statusMessage && (
          <div style={{
            background: "#1e293b",
            border: "1px solid #38bdf8",
            color: "#38bdf8",
            padding: "10px 14px",
            borderRadius: 8,
            fontSize: "0.85rem",
            marginBottom: 16
          }}>
            {statusMessage}
          </div>
        )}

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>
          {/* SELETTORE VERBOSITÀ */}
          <div style={{ background: "#0f172a", border: "1px solid #334155", borderRadius: 8, padding: 16 }}>
            <label style={{ display: "block", color: "#cbd5e1", fontSize: "0.88rem", fontWeight: 600, marginBottom: 8 }}>
              Livello Verbosità Log (Debug):
            </label>
            <select
              value={minLevel}
              disabled={isLoading || isSaving}
              onChange={(e) => void handleSaveLevel(e.target.value as AppLogLevel)}
              style={{
                width: "100%",
                background: "#1e293b",
                border: "1px solid #475569",
                color: "#f8fafc",
                padding: "8px 12px",
                borderRadius: 6,
                fontSize: "0.9rem",
                fontWeight: 600,
                outline: "none"
              }}
            >
              <option value="Trace">🔍 Trace - Massimo Verboso (Predefinito - Consigliato per Agente)</option>
              <option value="Debug">🐛 Debug - Dettagliato</option>
              <option value="Information">ℹ️ Information - Informativo Generico</option>
              <option value="Warning">⚠️ Warning - Solo Avvisi ed Errori</option>
              <option value="Error">❌ Error - Solo Errori Critici</option>
              <option value="None">🚫 None - Log Disattivati</option>
            </select>
            <span style={{ display: "block", color: "#64748b", fontSize: "0.78rem", marginTop: 8 }}>
              Impostato di default su <strong>Trace (Massimo Verboso)</strong> per catturare ogni passaggio, tool call ed eventuale errore dell'agente.
            </span>
          </div>

          {/* STATISTICHE SPAZIO SU DISCO */}
          <div style={{ background: "#0f172a", border: "1px solid #334155", borderRadius: 8, padding: 16 }}>
            <span style={{ display: "block", color: "#cbd5e1", fontSize: "0.88rem", fontWeight: 600, marginBottom: 8 }}>
              Spazio Occupato dai Log:
            </span>
            <div style={{ display: "flex", alignItems: "baseline", gap: 10 }}>
              <span style={{ fontSize: "1.6rem", fontWeight: 700, color: "#38bdf8" }}>
                {storageInfo ? storageInfo.formattedSize : "... KB"}
              </span>
              <span style={{ color: "#94a3b8", fontSize: "0.82rem" }}>
                ({storageInfo ? `${storageInfo.memoryEntryCount} voci in memoria, ${storageInfo.fileCount} file su disco` : "caricamento..."})
              </span>
            </div>

            <div style={{ marginTop: 12, display: "flex", gap: 8 }}>
              <button
                type="button"
                onClick={() => void handleClearLogs()}
                disabled={isLoading || isSaving}
                style={{
                  background: "linear-gradient(135deg, #991b1b 0%, #7f1d1d 100%)",
                  color: "#fecaca",
                  border: "1px solid #ef4444",
                  borderRadius: 6,
                  padding: "6px 12px",
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  cursor: "pointer"
                }}
              >
                🗑️ Cancella / Azzera Log
              </button>
              <button
                type="button"
                onClick={() => void loadData()}
                disabled={isLoading}
                style={{
                  background: "#1e293b",
                  color: "#94a3b8",
                  border: "1px solid #475569",
                  borderRadius: 6,
                  padding: "6px 12px",
                  fontSize: "0.82rem",
                  cursor: "pointer"
                }}
              >
                🔄 Aggiorna Metadati
              </button>
            </div>
          </div>
        </div>

        {storageInfo && (
          <div style={{ marginTop: 12, fontSize: "0.78rem", color: "#64748b" }}>
            📁 Percorso file di log: <code style={{ color: "#93c5fd" }}>{storageInfo.logDirectory}</code>
          </div>
        )}
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
