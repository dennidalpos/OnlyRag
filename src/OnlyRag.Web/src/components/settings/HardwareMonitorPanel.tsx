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
import { Cpu, HardDrive, Zap, RefreshCw, Database } from "lucide-react";

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
    const interval = setInterval(() => {
      void fetchTelemetry();
    }, 4000);
    return () => clearInterval(interval);
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
          <Zap className="icon" style={{ color: "#3b82f6" }} /> Monitoraggio Hardware & Profilo Energetico
        </h3>
        <button className="button button--ghost button--sm" onClick={() => void fetchTelemetry()}>
          <RefreshCw className="icon icon--sm" /> Aggiorna
        </button>
      </div>

      {error && <div className="feedback-banner feedback-banner--error">{error}</div>}

      {metrics && (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: "16px", marginBottom: "20px" }}>
          <div className="telemetry-card" style={{ background: "var(--color-bg-secondary, #f8fafc)", padding: "14px", borderRadius: "8px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "8px", color: "var(--color-text-secondary, #64748b)", fontSize: "0.85rem" }}>
              <Cpu size={16} /> Carico CPU
            </div>
            <div style={{ fontSize: "1.4rem", fontWeight: 700, margin: "8px 0" }}>{metrics.cpuUsagePercentage}%</div>
            <div style={{ height: "6px", width: "100%", background: "#e2e8f0", borderRadius: "3px", overflow: "hidden" }}>
              <div
                style={{
                  height: "100%",
                  width: `${Math.min(100, metrics.cpuUsagePercentage)}%`,
                  background: metrics.cpuUsagePercentage > 85 ? "#ef4444" : metrics.cpuUsagePercentage > 60 ? "#f59e0b" : "#10b981",
                  transition: "width 0.3s ease"
                }}
              />
            </div>
          </div>

          <div className="telemetry-card" style={{ background: "var(--color-bg-secondary, #f8fafc)", padding: "14px", borderRadius: "8px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "8px", color: "var(--color-text-secondary, #64748b)", fontSize: "0.85rem" }}>
              <HardDrive size={16} /> Memoria RAM
            </div>
            <div style={{ fontSize: "1.4rem", fontWeight: 700, margin: "8px 0" }}>
              {metrics.memoryAvailableMB} MB libera / {metrics.memoryTotalMB} MB
            </div>
            <div style={{ fontSize: "0.8rem", color: "var(--color-text-muted, #94a3b8)" }}>
              Stato Carico: <strong>{metrics.loadLevel}</strong>
            </div>
          </div>

          <div className="telemetry-card" style={{ background: "var(--color-bg-secondary, #f8fafc)", padding: "14px", borderRadius: "8px" }}>
            <div style={{ display: "flex", alignItems: "center", gap: "8px", color: "var(--color-text-secondary, #64748b)", fontSize: "0.85rem" }}>
              <Zap size={16} /> Alimentazione
            </div>
            <div style={{ fontSize: "1.2rem", fontWeight: 600, margin: "8px 0" }}>
              {metrics.powerSource === "ACPower" ? "Alimentatore Rete (AC)" : `Batteria (${metrics.batteryPercentage ?? "?"}%)`}
            </div>
            <div style={{ fontSize: "0.8rem", color: "var(--color-text-muted, #94a3b8)" }}>
              Throttling Job: max {metrics.recommendedMaxWorkers} worker ({metrics.recommendedDelayMs}ms delay)
            </div>
          </div>
        </div>
      )}

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
