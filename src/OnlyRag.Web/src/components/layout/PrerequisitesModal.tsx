import React, { useState } from "react";
import type { OcrProvisionStatus, OcrStartupAnalysis } from "../../api";
import { ProgressBar } from "../common/ProgressBar";

export type PrerequisitesModalProps = {
  isOpen: boolean;
  onClose: () => void;
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  isConfiguring: boolean;
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void;
  onCancelOcr: () => void;
  onInstallOllama?: () => void;
  ollamaInstalled?: boolean;
  onOpenLibreOfficeDownload?: () => void;
  libreOfficeInstalled?: boolean;
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
  ollamaInstalled = true,
  onOpenLibreOfficeDownload,
  libreOfficeInstalled = false
}) => {
  const [activeOcrTab, setActiveOcrTab] = useState<"vision" | "paddle">("paddle");
  const [selectedTarget, setSelectedTarget] = useState<"auto" | "cpu" | "nvidia">("auto");
  const [visionActivated, setVisionActivated] = useState(false);

  if (!isOpen) return null;

  const isRunning = Boolean(ocrProvisionStatus?.isRunning || isConfiguring);
  const progressPercent = ocrProvisionStatus?.progressPercent ?? 0;
  const currentStep = ocrProvisionStatus?.stepIndex ?? 0;
  const totalSteps = ocrProvisionStatus?.stepCount ?? 8;
  const stepLabel = ocrProvisionStatus?.stepLabel || (isRunning ? "Installazione in corso..." : "In attesa");
  const isConfigured = Boolean(ocrProvisionStatus?.isConfigured || visionActivated);

  const hasDiskSpace = ocrAnalysis?.hasMinimumDiskSpace !== false;

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
          maxWidth: "640px",
          maxHeight: "90vh",
          overflowY: "auto",
          padding: "24px",
          boxShadow: "0 25px 50px -12px rgba(0, 0, 0, 0.5)",
          color: "#f8fafc"
        }}
      >
        {/* Header */}
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
          Verifica e installa i moduli minimi necessari per il corretto funzionamento di OnlyRag.
        </p>

        {/* 1. SEZIONE MODULI MINIMI RICHIESTI */}
        <div style={{ background: "rgba(15, 23, 42, 0.5)", borderRadius: "12px", padding: "14px", marginBottom: "20px", border: "1px solid #334155" }}>
          <h3 style={{ fontSize: "0.9rem", textTransform: "uppercase", letterSpacing: "0.05em", color: "#94a3b8", marginTop: 0, marginBottom: "12px" }}>
            📦 Moduli Minimi Richiesti per l'App
          </h3>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "10px" }}>
            {/* Ollama AI Service */}
            <div style={{ background: "#1e293b", padding: "10px 12px", borderRadius: "8px", border: "1px solid #334155" }}>
              <div style={{ fontSize: "0.825rem", fontWeight: 600, color: "#cbd5e1" }}>Motore AI LLM (Ollama)</div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: "6px" }}>
                <span style={{ fontSize: "0.75rem", fontWeight: 600, color: ollamaInstalled ? "#4ade80" : "#fbbf24" }}>
                  {ollamaInstalled ? "✓ Disponibile" : "⚠️ Non installato"}
                </span>
                {!ollamaInstalled && onInstallOllama && (
                  <button
                    type="button"
                    onClick={onInstallOllama}
                    disabled={isRunning}
                    style={{ fontSize: "0.75rem", padding: "3px 8px", background: "#0284c7", color: "#fff", border: "none", borderRadius: "4px", cursor: "pointer" }}
                  >
                    Installa Ollama
                  </button>
                )}
              </div>
            </div>

            {/* OCR Module */}
            <div style={{ background: "#1e293b", padding: "10px 12px", borderRadius: "8px", border: "1px solid #334155" }}>
              <div style={{ fontSize: "0.825rem", fontWeight: 600, color: "#cbd5e1" }}>Motore OCR (Vision / Paddle)</div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: "6px" }}>
                <span style={{ fontSize: "0.75rem", fontWeight: 600, color: isConfigured ? "#4ade80" : "#fbbf24" }}>
                  {isConfigured ? "✓ Modulo OCR Configurato" : "⚠️ OCR da configurare"}
                </span>
              </div>
            </div>

            {/* LibreOffice PDF Export */}
            <div style={{ background: "#1e293b", padding: "10px 12px", borderRadius: "8px", border: "1px solid #334155" }}>
              <div style={{ fontSize: "0.825rem", fontWeight: 600, color: "#cbd5e1" }}>Esportazione PDF (LibreOffice)</div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: "6px" }}>
                <span style={{ fontSize: "0.75rem", fontWeight: 600, color: libreOfficeInstalled ? "#4ade80" : "#94a3b8" }}>
                  {libreOfficeInstalled ? "✓ Installato" : "⚠️ Opzionale (PDF)"}
                </span>
                {!libreOfficeInstalled && onOpenLibreOfficeDownload && (
                  <button
                    type="button"
                    onClick={onOpenLibreOfficeDownload}
                    style={{ fontSize: "0.75rem", padding: "3px 8px", background: "#334155", color: "#f8fafc", border: "1px solid #475569", borderRadius: "4px", cursor: "pointer" }}
                  >
                    Scarica LibreOffice
                  </button>
                )}
              </div>
            </div>

            {/* Disk Space */}
            <div style={{ background: "#1e293b", padding: "10px 12px", borderRadius: "8px", border: "1px solid #334155" }}>
              <div style={{ fontSize: "0.825rem", fontWeight: 600, color: "#cbd5e1" }}>Spazio su Disco (&gt;2GB)</div>
              <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginTop: "6px" }}>
                <span style={{ fontSize: "0.75rem", fontWeight: 600, color: hasDiskSpace ? "#4ade80" : "#ef4444" }}>
                  {hasDiskSpace ? "✓ Spazio Sufficiente" : "⚠️ Spazio Insufficiente"}
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* 2. SEZIONE FLUSSI OCR DISTINTI (OCR Vision vs OCR Paddle) */}
        <div style={{ marginBottom: "20px" }}>
          <label style={{ display: "block", fontSize: "0.875rem", fontWeight: 600, marginBottom: "8px", color: "#cbd5e1" }}>
            Seleziona Flusso OCR:
          </label>
          <div style={{ display: "flex", gap: "10px", marginBottom: "16px" }}>
            <button
              type="button"
              onClick={() => setActiveOcrTab("paddle")}
              style={{
                flex: 1,
                padding: "10px 14px",
                borderRadius: "8px",
                border: activeOcrTab === "paddle" ? "2px solid #38bdf8" : "1px solid #334155",
                background: activeOcrTab === "paddle" ? "rgba(56, 189, 248, 0.15)" : "#0f172a",
                color: activeOcrTab === "paddle" ? "#38bdf8" : "#94a3b8",
                fontWeight: 600,
                fontSize: "0.85rem",
                cursor: "pointer"
              }}
            >
              🐍 OCR Paddle (Python)
            </button>
            <button
              type="button"
              onClick={() => setActiveOcrTab("vision")}
              style={{
                flex: 1,
                padding: "10px 14px",
                borderRadius: "8px",
                border: activeOcrTab === "vision" ? "2px solid #38bdf8" : "1px solid #334155",
                background: activeOcrTab === "vision" ? "rgba(56, 189, 248, 0.15)" : "#0f172a",
                color: activeOcrTab === "vision" ? "#38bdf8" : "#94a3b8",
                fontWeight: 600,
                fontSize: "0.85rem",
                cursor: "pointer"
              }}
            >
              ⚡ OCR Vision (ONNX DirectML)
            </button>
          </div>

          {/* TAB FLUSSO 1: OCR PADDLE (Python 3.10-3.13 + PaddleOCR) */}
          {activeOcrTab === "paddle" && (
            <div style={{ background: "rgba(30, 41, 59, 0.6)", padding: "16px", borderRadius: "10px", border: "1px solid #475569" }}>
              <div style={{ fontSize: "0.85rem", color: "#94a3b8", marginBottom: "12px" }}>
                Flusso completo con ambiente Python dedicato, PaddleOCR PP-OCRv5 e accelerazione GPU CUDA o CPU.
              </div>

              {/* Target Selector */}
              <div style={{ marginBottom: "14px" }}>
                <label style={{ display: "block", fontSize: "0.8rem", fontWeight: 600, marginBottom: "6px", color: "#cbd5e1" }}>
                  Target Runtime Paddle:
                </label>
                <div style={{ display: "flex", gap: "12px" }}>
                  <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.825rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
                    <input
                      type="radio"
                      name="runtimeTarget"
                      value="auto"
                      checked={selectedTarget === "auto"}
                      onChange={() => setSelectedTarget("auto")}
                      disabled={isRunning}
                    />
                    Auto (Rilevamento GPU)
                  </label>
                  <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.825rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
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
                  <label style={{ display: "flex", alignItems: "center", gap: "6px", fontSize: "0.825rem", cursor: isRunning ? "not-allowed" : "pointer" }}>
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

              {/* Steps Progress */}
              <div style={{ marginTop: "12px" }}>
                <div style={{ display: "flex", justifyContent: "space-between", fontSize: "0.825rem", fontWeight: 600, marginBottom: "6px" }}>
                  <span style={{ color: isRunning ? "#38bdf8" : isConfigured ? "#4ade80" : "#cbd5e1" }}>
                    {stepLabel}
                  </span>
                  <span style={{ color: "#94a3b8" }}>
                    {isRunning ? `Passo ${currentStep}/${totalSteps} (${progressPercent}%)` : isConfigured ? "100%" : "0%"}
                  </span>
                </div>

                <ProgressBar label={stepLabel} value={isRunning ? progressPercent : isConfigured ? 100 : 0} />

                {ocrProvisionStatus?.message && (
                  <p style={{ marginTop: "10px", fontSize: "0.825rem", color: "#e2e8f0", background: "rgba(15, 23, 42, 0.4)", padding: "8px 12px", borderRadius: "6px" }}>
                    {ocrProvisionStatus.message}
                  </p>
                )}

                {ocrProvisionStatus?.runtimeDetail && (
                  <details style={{ marginTop: "8px", fontSize: "0.8rem", color: "#94a3b8" }}>
                    <summary style={{ cursor: "pointer" }}>Dettaglio log runtime</summary>
                    <pre style={{ whiteSpace: "pre-wrap", background: "#0f172a", padding: "8px", borderRadius: "6px", marginTop: "4px", fontSize: "0.75rem" }}>
                      {ocrProvisionStatus.runtimeDetail}
                    </pre>
                  </details>
                )}
              </div>
            </div>
          )}

          {/* TAB FLUSSO 2: OCR VISION (ONNX DirectML Nativo C#) */}
          {activeOcrTab === "vision" && (
            <div style={{ background: "rgba(30, 41, 59, 0.6)", padding: "16px", borderRadius: "10px", border: "1px solid #475569" }}>
              <div style={{ fontSize: "0.85rem", color: "#94a3b8", marginBottom: "12px" }}>
                Architettura nativa C# basata su ONNX DirectML. Sfrutta l'accelerazione GPU integrata di Windows per l'elaborazione locale ad alte prestazioni.
              </div>

              {/* Steps indicators for Vision OCR */}
              <div style={{ display: "flex", flexDirection: "column", gap: "8px", margin: "14px 0" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", fontSize: "0.825rem", color: "#cbd5e1" }}>
                  <span style={{ width: "20px", height: "20px", borderRadius: "50%", background: "#0284c7", color: "#fff", display: "inline-flex", alignItems: "center", justifyContent: "center", fontSize: "0.75rem", fontWeight: 700 }}>1</span>
                  Rilevamento accelerazione GPU DirectML e dispositivo grafico
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", fontSize: "0.825rem", color: "#cbd5e1" }}>
                  <span style={{ width: "20px", height: "20px", borderRadius: "50%", background: "#0284c7", color: "#fff", display: "inline-flex", alignItems: "center", justifyContent: "center", fontSize: "0.75rem", fontWeight: 700 }}>2</span>
                  Verifica binding ONNX Runtime Native C# &amp; Model Cache
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "10px", fontSize: "0.825rem", color: visionActivated ? "#4ade80" : "#cbd5e1" }}>
                  <span style={{ width: "20px", height: "20px", borderRadius: "50%", background: visionActivated ? "#4ade80" : "#0284c7", color: "#fff", display: "inline-flex", alignItems: "center", justifyContent: "center", fontSize: "0.75rem", fontWeight: 700 }}>3</span>
                  OCR Vision Attivo e Pronto
                </div>
              </div>

              <div style={{ display: "flex", gap: "8px", marginTop: "12px" }}>
                <span style={{ padding: "4px 8px", borderRadius: "6px", background: "rgba(74, 222, 128, 0.2)", color: "#4ade80", fontSize: "0.75rem", fontWeight: 600 }}>
                  ✓ Esecuzione Nativa C#
                </span>
                <span style={{ padding: "4px 8px", borderRadius: "6px", background: "rgba(56, 189, 248, 0.2)", color: "#38bdf8", fontSize: "0.75rem", fontWeight: 600 }}>
                  ⚡ Accelerazione GPU DirectML
                </span>
              </div>
            </div>
          )}
        </div>

        {/* Action Controls */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: "10px", paddingTop: "12px", borderTop: "1px solid #334155" }}>
          {activeOcrTab === "vision" ? (
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
              onClick={() => setVisionActivated(true)}
            >
              {visionActivated ? "✓ OCR Vision Attivato" : "Attiva OCR Vision (DirectML)"}
            </button>
          ) : isRunning ? (
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
              {isConfigured ? "Ripara / Reinstalla OCR Paddle" : "Avvia Installazione OCR Paddle"}
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
