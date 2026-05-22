import { useRef } from "react";
import type { OcrStartupAnalysis } from "../api";
import { formatTelemetryBytes } from "./SettingsSection.formatting";
import { useModalFocusTrap } from "./useModalFocusTrap";

type OcrStartupPromptProps = {
  analysis: OcrStartupAnalysis | null;
  isConfiguring: boolean;
  onConfirm: () => void;
  onDismiss: () => void;
  onOpenSettings: () => void;
};

export function OcrStartupPrompt({
  analysis,
  isConfiguring,
  onConfirm,
  onDismiss,
  onOpenSettings
}: OcrStartupPromptProps) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const isOpen = Boolean(analysis?.shouldPrompt);

  useModalFocusTrap(modalRef, isOpen);

  if (!analysis?.shouldPrompt) {
    return null;
  }

  return (
    <div className="setup-gate-backdrop">
      <div
        className="setup-gate-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="ocr-startup-title"
        ref={modalRef}
        tabIndex={-1}
      >
        <h2 id="ocr-startup-title">{analysis.title}</h2>
        <h3>Runtime consigliato: {analysis.recommendedRuntimeTarget === "nvidia" ? "NVIDIA GPU" : "CPU"}</h3>
        <p>{analysis.message}</p>
        <p>
          Spazio disponibile: {formatTelemetryBytes(analysis.availableDiskBytes)} di{" "}
          {formatTelemetryBytes(analysis.requiredDiskBytes)} richiesti.
        </p>
        <div className="settings-actions">
          <button type="button" onClick={onConfirm} disabled={isConfiguring}>
            {isConfiguring ? "Configurazione OCR..." : "Configura OCR"}
          </button>
          <button type="button" className="button-secondary" onClick={onOpenSettings} disabled={isConfiguring}>
            Apri Impostazioni
          </button>
          <button type="button" className="button-secondary" onClick={onDismiss} disabled={isConfiguring}>
            Piu tardi
          </button>
        </div>
      </div>
    </div>
  );
}
