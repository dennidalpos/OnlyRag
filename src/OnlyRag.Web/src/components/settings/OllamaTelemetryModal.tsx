import React from "react";
import type { DiagnosticsResponse } from "../../api";
import { Cpu, HardDrive, Monitor, Server, X } from "lucide-react";

type OllamaTelemetryModalProps = {
  isOpen: boolean;
  onClose: () => void;
  diagnostics: DiagnosticsResponse | null;
};

export const OllamaTelemetryModal: React.FC<OllamaTelemetryModalProps> = ({
  isOpen,
  onClose,
  diagnostics
}) => {
  if (!isOpen || !diagnostics) return null;

  const telemetry = diagnostics.systemTelemetry;
  const runningModels = diagnostics.ollamaRunningModels ?? [];

  return (
    <div
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(15, 23, 42, 0.75)",
        backdropFilter: "blur(4px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1100,
        padding: "20px"
      }}
    >
      <div
        className="card"
        style={{
          width: "100%",
          maxWidth: "750px",
          maxHeight: "85vh",
          display: "flex",
          flexDirection: "column",
          background: "#0f172a",
          border: "1px solid #334155",
          borderRadius: "12px",
          overflow: "hidden",
          color: "#f8fafc",
          boxShadow: "0 25px 50px -12px rgba(0, 0, 0, 0.5)"
        }}
      >
        {/* Header */}
        <div
          style={{
            padding: "16px 20px",
            borderBottom: "1px solid #1e293b",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            background: "#1e293b"
          }}
        >
          <h3 style={{ margin: 0, display: "flex", alignItems: "center", gap: 8, fontSize: "1.05rem", color: "#60a5fa" }}>
            <Server size={18} /> Parametri OS &amp; Telemetria Client Ollama
          </h3>
          <button
            type="button"
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#94a3b8", cursor: "pointer", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Content */}
        <div style={{ padding: "20px", overflowY: "auto", display: "flex", flexDirection: "column", gap: 16 }}>
          {/* OS & Hardware Section */}
          <div style={{ background: "#1e293b", borderRadius: 8, padding: 14, border: "1px solid #334155" }}>
            <h4 style={{ margin: "0 0 10px 0", fontSize: "0.9rem", color: "#93c5fd", display: "flex", alignItems: "center", gap: 6 }}>
              <Cpu size={15} /> Sistema Operativo &amp; Risorse Hardware
            </h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 10, fontSize: "0.85rem" }}>
              <div>
                <span style={{ color: "#94a3b8" }}>Versione App:</span>{" "}
                <strong>v{diagnostics.appVersion}</strong>
              </div>
              <div>
                <span style={{ color: "#94a3b8" }}>Database Path:</span>{" "}
                <code style={{ fontSize: "0.78rem", background: "#0f172a", padding: "2px 6px", borderRadius: 4 }}>
                  {diagnostics.databasePath}
                </code>
              </div>
              <div>
                <span style={{ color: "#94a3b8" }}>Cartella Log:</span>{" "}
                <code style={{ fontSize: "0.78rem", background: "#0f172a", padding: "2px 6px", borderRadius: 4 }}>
                  {diagnostics.logsDirectory}
                </code>
              </div>
              {telemetry?.gpu?.name && (
                <div>
                  <span style={{ color: "#94a3b8" }}>GPU NVIDIA:</span>{" "}
                  <strong>{telemetry.gpu.name} ({telemetry.gpu.driverVersion ?? "Driver OK"})</strong>
                </div>
              )}
            </div>
          </div>

          {/* Ollama Status Section */}
          <div style={{ background: "#1e293b", borderRadius: 8, padding: 14, border: "1px solid #334155" }}>
            <h4 style={{ margin: "0 0 10px 0", fontSize: "0.9rem", color: "#34d399", display: "flex", alignItems: "center", gap: 6 }}>
              <Monitor size={15} /> Servizio Local Ollama Client
            </h4>
            <div style={{ display: "flex", flexDirection: "column", gap: 8, fontSize: "0.85rem" }}>
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <span style={{ color: "#94a3b8" }}>Stato Connessione:</span>
                <span style={{
                  padding: "2px 8px", borderRadius: 4, fontWeight: 600, fontSize: "0.8rem",
                  background: diagnostics.ollamaIsReachable ? "rgba(52, 211, 153, 0.2)" : "rgba(248, 113, 113, 0.2)",
                  color: diagnostics.ollamaIsReachable ? "#34d399" : "#f87171"
                }}>
                  {diagnostics.ollamaStatus}
                </span>
                {diagnostics.ollamaVersion && <span style={{ color: "#cbd5e1" }}>v{diagnostics.ollamaVersion}</span>}
              </div>

              {runningModels.length > 0 ? (
                <div>
                  <div style={{ fontWeight: 600, color: "#cbd5e1", marginBottom: 6 }}>
                    Modelli Caricati in VRAM ({runningModels.length}):
                  </div>
                  <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                    {runningModels.map((m, idx) => (
                      <div key={idx} style={{ background: "#0f172a", padding: "8px 12px", borderRadius: 6, display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                        <span style={{ fontWeight: 600, color: "#60a5fa" }}>{m.name}</span>
                        <div style={{ fontSize: "0.78rem", color: "#94a3b8", display: "flex", gap: 12 }}>
                          {m.contextLength && <span>Context: {m.contextLength.toLocaleString("it-IT")} token</span>}
                          {m.sizeVram && <span>VRAM: {Math.round(m.sizeVram / 1024 / 1024)} MB</span>}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div style={{ color: "#64748b", fontStyle: "italic" }}>
                  Nessun modello Ollama attualmente attivo in memoria RAM/VRAM.
                </div>
              )}
            </div>
          </div>

          {/* Qdrant & Vector Index */}
          <div style={{ background: "#1e293b", borderRadius: 8, padding: 14, border: "1px solid #334155" }}>
            <h4 style={{ margin: "0 0 10px 0", fontSize: "0.9rem", color: "#fbbf24", display: "flex", alignItems: "center", gap: 6 }}>
              <HardDrive size={15} /> Qdrant &amp; Indice Vettoriale Locale
            </h4>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 10, fontSize: "0.85rem" }}>
              <div>
                <span style={{ color: "#94a3b8" }}>Stato Qdrant:</span>{" "}
                <strong>{diagnostics.qdrant.status}</strong>
              </div>
              <div>
                <span style={{ color: "#94a3b8" }}>gRPC Endpoint:</span>{" "}
                <code>{diagnostics.qdrant.grpcEndpoint}</code>
              </div>
            </div>
          </div>
        </div>

        {/* Footer */}
        <div style={{ padding: "12px 20px", borderTop: "1px solid #1e293b", display: "flex", justifyContent: "flex-end", background: "#1e293b" }}>
          <button
            type="button"
            className="button button--secondary button--sm"
            onClick={onClose}
          >
            Chiudi
          </button>
        </div>
      </div>
    </div>
  );
};
