import { useEffect, useState } from "react";
import {
  HardwareMetricsResponse,
  SqliteDatabaseStatus,
  SqliteMaintenanceResult,
  apiRequest,
  getDatabaseStatus,
  runDatabaseMaintenance,
  setHardwareEnergyProfile
} from "../../apiClient";
import { Zap, RefreshCw, Database } from "lucide-react";

export function HardwareMonitorPanel() {
  const [metrics, setMetrics] = useState<HardwareMetricsResponse | null>(null);
  const [dbStatus, setDbStatus] = useState<SqliteDatabaseStatus | null>(null);
  const [isChangingProfile, setIsChangingProfile] = useState(false);
  const [isRunningMaintenance, setIsRunningMaintenance] = useState(false);
  const [maintResult, setMaintResult] = useState<SqliteMaintenanceResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function fetchTelemetry() {
    try {
      const hData = await apiRequest<HardwareMetricsResponse>("/api/system/hardware");
      setMetrics(hData);
      const dData = await getDatabaseStatus();
      setDbStatus(dData);
      setError(null);
    } catch (err) {
      setError((err as Error).message);
    }
  }

  useEffect(() => {
    void fetchTelemetry();
  }, []);

  async function handleProfileChange(profile: "Performance" | "Balanced" | "Eco") {
    try {
      setIsChangingProfile(true);
      const updated = await setHardwareEnergyProfile(profile);
      setMetrics(updated);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsChangingProfile(false);
    }
  }

  async function handleRunDbMaintenance() {
    try {
      setIsRunningMaintenance(true);
      const res = await runDatabaseMaintenance();
      setMaintResult(res);
      const dData = await getDatabaseStatus();
      setDbStatus(dData);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setIsRunningMaintenance(false);
    }
  }

  return (
    <div className="card card--spaced" style={{ padding: "20px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
        <h3 style={{ margin: 0, display: "flex", alignItems: "center", gap: "8px" }}>
          <Zap className="icon" style={{ color: "#3b82f6" }} /> Profilo Energetico & Manutenzione Database
        </h3>
        <button className="button button--ghost button--sm" onClick={() => void fetchTelemetry()}>
          <RefreshCw className="icon icon--sm" /> Aggiorna
        </button>
      </div>

      {error && <div className="feedback-banner feedback-banner--error">{error}</div>}

      <div style={{ marginBottom: "24px" }}>
        <label style={{ display: "block", fontWeight: 600, marginBottom: "8px" }}>Seleziona Profilo Energetico:</label>
        <div style={{ display: "flex", gap: "10px" }}>
          {(["Performance", "Balanced", "Eco"] as const).map((p) => (
            <button
              key={p}
              disabled={isChangingProfile}
              className={`button ${metrics?.activeProfile === p ? "button--primary" : "button--secondary"}`}
              onClick={() => void handleProfileChange(p)}
            >
              {p === "Performance" ? "🚀 Prestazioni" : p === "Balanced" ? "⚖️ Bilanciato" : "🍃 Eco / Batteria"}
            </button>
          ))}
        </div>
      </div>

      <hr style={{ border: "none", borderTop: "1px solid var(--color-border, #e2e8f0)", margin: "20px 0" }} />

      {dbStatus && (
        <div>
          <h4 style={{ margin: "0 0 12px 0", display: "flex", alignItems: "center", gap: "8px" }}>
            <Database className="icon" /> Manutenzione & Compattazione Database SQLite
          </h4>
          <div style={{ fontSize: "0.9rem", marginBottom: "12px", color: "var(--color-text-secondary, #64748b)" }}>
            Dimensione DB: <strong>{dbStatus.formattedFileSize}</strong> | FTS5 Attivo: <strong>{dbStatus.fts5Enabled ? "Sì" : "No"}</strong>
            {dbStatus.lastMaintenanceAtUtc && (
              <span> | Ultima manutenzione: {new Date(dbStatus.lastMaintenanceAtUtc).toLocaleString()}</span>
            )}
          </div>
          <button
            disabled={isRunningMaintenance}
            className="button button--secondary"
            onClick={() => void handleRunDbMaintenance()}
          >
            {isRunningMaintenance ? "Compattazione in corso..." : "Esegui Manutenzione VACUUM & FTS5"}
          </button>
          {maintResult && (
            <div className="feedback-banner feedback-banner--success" style={{ marginTop: "12px" }}>
              {maintResult.message}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
