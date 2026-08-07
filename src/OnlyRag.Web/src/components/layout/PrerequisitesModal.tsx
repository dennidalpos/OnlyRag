import React, { useState } from "react";
import type { OcrProvisionStatus, OcrStartupAnalysis } from "../../api";
import { ProgressBar } from "../common/ProgressBar";

type PrerequisitesModalProps = {
  isOpen: boolean;
  onClose: () => void;
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  isConfiguring: boolean;
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void;
  onCancelOcr: () => void;
  onInstallOllama?: () => void;
  ollamaInstalled?: boolean;
};

export const PrerequisitesModal: React.FC<PrerequisitesModalProps> = ({
  isOpen,
  onClose,
  ocrAnalysis,
  ocrProvisionStatus,
  isConfiguring,
  onConfigureOcr,
  onCancelOcr,
  onInstallOllama,
  ollamaInstalled = true
}) => {
  const [selectedTarget, setSelectedTarget] = useState<"auto" | "cpu" | "nvidia">("auto");

  if (!isOpen) return null;

  const isRunning = Boolean(ocrProvisionStatus?.isRunning || isConfiguring);
  const progressPercent = ocrProvisionStatus?.progressPercent ?? 0;
  const currentStep = ocrProvisionStatus?.stepIndex ?? 0;
  const totalSteps = ocrProvisionStatus?.stepCount ?? 8;
  const stepLabel = ocrProvisionStatus?.stepLabel || (isRunning ? "Installazione in corso..." : "In attesa");
  const isConfigured = ocrProvisionStatus?.isConfigured;

  return (
    <div
      className="modal-overlay animate-fade-in"
      role="dialog"
      aria-modal="true"
      aria-labelledby="prerequisites-modal-title"
      style={{
        position: "fixed",
        inset: 0,
        zIndex: 9999,
        background: "rgba(15, 23, 42, 0.85)",
        backdropFilter: "blur(12px)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "16px"
      }}
    >
      <div
        className="modal-card"
        style={{
          background: "#1e293b",
          border: "1px solid #334155",
          borderRadius: "16px",
          width: "100%",
          maxWidth: "600px",
          padding: "24px",
          boxShadow: "0 25px 50px -12px rgba(0, 0, 0, 0.5)",
          color: "#f8fafc"
        }}
      >
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "16px" }}>
          <h2 id="prerequisites-modal-title" style={{ margin: 0, fontSize: "1.35rem", fontWeight: 700, color: "#38bdf8" }}>
            ⚙️ Dialog Installazione Prerequisiti
          </h2>
          <button
            type="button"
            className="button-secondary"
            onClick={onClose}
            style={{ padding: "4px 10px", fontSize: "0.85rem", cursor: "pointer" }}
            aria-label="Chiudi dialogo"
          >
            ✕
          </button>
        </div>

        <p style={{ color: "#94a3b8", fontSize: "0.875rem", marginTop: 0, marginBottom: "20px" }}>
          Configura OCR e dipendenze AI senza finestre di PowerShell esterne.
        </p>

        {/* Runtime Target Selection */}
        <div style={{ background: "rgba(30, 41, 59, 0.6)", padding: "14px", borderRadius: "10px", marginBottom: "20px", border: "1px solid #475569" }}>
          <label style={{ display: "block", fontSize: "0.85rem", fontWeight: 600, marginBottom: "8px", color: "#cbd5e1" }}>
            Modalità di elaborazione OCR / Moduli:
          </label>
          <div style={{ display: "flex", gap: "12px" }}>
            <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
              <input
                type="radio"
                name="runtimeTarget"
                value="auto"
                checked={selectedTarget === "auto"}
                onChange={() => setSelectedTarget("auto")}
                disabled={isRunning}
              />
              Auto (Consigliato)
            </label>
            <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
              <input
                type="radio"
                name="runtimeTarget"
                value="nvidia"
                checked={selectedTarget === "nvidia"}
                onChange={() => setSelectedTarget("nvidia")}
                disabled={isRunning}
              />
              GPU NVIDIA (CUDA)
            </label>
            <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.85rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
              <input
                type="radio"
                name="runtimeTarget"
                value="cpu"
                checked={selectedTarget === "cpu"}
                onChange={() => setSelectedTarget("cpu")}
                disabled={isRunning}
              />
              CPU Standard
            </label>
          </div>
        </div>

        {/* Status & Progress Info */}
        <div style={{ marginBottom: "20px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.85rem", fontWeight: 600, marginBottom: "6px" }}>
            <span style={{ color: isRunning ? "#38bdf8" : isConfigured ? "#4ade80" : "#cbd5e1" }}>
              {stepLabel}
            </span>
            <span style={{ color: "#94a3b8" }}>
              {isRunning ? `Passo ${currentStep}/${totalSteps} (${progressPercent}%)` : isConfigured ? "100%" : "0%"}
            </span>
          </div>

          <ProgressBar label={stepLabel} value={isRunning ? progressPercent : isConfigured ? 100 : 0} />

          {ocrProvisionStatus?.message && (
            <p style={{ marginTop: "10px", fontSize: "0.85rem", color: "#e2e8f0", background: "rgba(15, 23, 42, 0.4)", padding: "10px", borderRadius: "6px" }}>
              {ocrProvisionStatus.message}
            </p>
          )}

          {ocrProvisionStatus?.runtimeDetail && (
            <details style={{ marginTop: "8px", fontSize: "0.8rem", color: "#94a3b8" }}>
              <summary style={{ cursor: "pointer" }}>Mostra dettaglio log runtime</summary>
              <pre style={{ whiteSpace: "pre-wrap", background: "#0f172a", padding: "8px", borderRadius: "6px", marginTop: "4px", fontSize: "0.75rem" }}>
                {ocrProvisionStatus.runtimeDetail}
              </pre>
            </details>
          )}
        </div>

        {/* Status badges */}
        <div style={{ display: "flex", gap: "8px", flexWrap: "wrap", marginBottom: "20px", fontSize: "0.8rem" }}>
          <span style={{ padding: "4px 8px", borderRadius: "6px", background: isConfigured ? "rgba(74, 222, 128, 0.2)" : "rgba(251, 191, 36, 0.2)", color: isConfigured ? "#4ade80" : "#fbbf24", fontWeight: 600 }}>
            {isConfigured ? "✓ Modulo OCR Pronto" : "⚠️ OCR da configurare"}
          </span>
          <span style={{ padding: "4px 8px", borderRadius: "6px", background: ollamaInstalled ? "rgba(74, 222, 128, 0.2)" : "rgba(251, 191, 36, 0.2)", color: ollamaInstalled ? "#4ade80" : "#fbbf24", fontWeight: 600 }}>
            {ollamaInstalled ? "✓ Ollama Disponibile" : "⚠️ Ollama non installato"}
          </span>
        </div>

        {/* Action Controls */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px", paddingTop: "12px", borderTop: "1px solid #334155" }}>
          {!ollamaInstalled && onInstallOllama && (
            <button
              type="button"
              className="button-secondary"
              onClick={onInstallOllama}
              disabled={isRunning}
              style={{ padding: "8px 16px", cursor: isRunning ? "not-allowed" : "pointer" }}
            >
              Installa Ollama
            </button>
          )}

          {isRunning ? (
            <button
              type="button"
              className="button-secondary"
              onClick={onCancelOcr}
              style={{ padding: "8px 16px", background: "#ef4444", color: "#fff", border: "none", borderRadius: "6px", cursor: "pointer" }}
            >
              Annulla Operazione
            </button>
          ) : (
            <button
              type="button"
              style={{
                padding: "8px 18px",
                background: "#0284c7",
                color: "#ffffff",
                border: "none",
                borderRadius: "6px",
                fontWeight: 600,
                cursor: "pointer"
              }}
              onClick={() => onConfigureOcr(selectedTarget)}
            >
              {isConfigured ? "Ripara / Reinstalla Prerequisiti" : "Avvia Installazione Prerequisiti"}
            </button>
          )}

          <button
            type="button"
            className="button-secondary"
            onClick={onClose}
            style={{ padding: "8px 16px", cursor: "pointer" }}
          >
            Chiudi
          </button>
        </div>
      </div>
    </div>
  );
};
