import { useState } from "react";
import type {
  OcrProvisionStatus,
  OcrStartupAnalysis,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse
} from "../api";
import { detectSetupIssues, detectSetupStatusItems, formatSetupTime } from "./SetupBanner.logic";

type SetupBannerProps = {
  ollamaStatus: OllamaStatusResponse | null;
  ollamaInstallStatus: OllamaInstallStatus | null;
  ollamaSettings: OllamaSettings | null;
  ollamaModels: OllamaModel[];
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  ocrLastCheckedAt?: Date | null;
  isChecking: boolean;
  isConfiguringOcr: boolean;
  onOpenSettings: () => void;
  onInstallOllama: () => void;
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void;
  onCancelOcr: () => void;
  onRecheck: () => void;
};

export function SetupBanner({
  ollamaStatus,
  ollamaInstallStatus,
  ollamaSettings,
  ollamaModels,
  ocrAnalysis,
  ocrProvisionStatus,
  ocrLastCheckedAt,
  isChecking,
  isConfiguringOcr,
  onOpenSettings,
  onInstallOllama,
  onConfigureOcr,
  onCancelOcr,
  onRecheck
}: SetupBannerProps) {
  const [dismissed, setDismissed] = useState(false);
  const issues = detectSetupIssues(
    ollamaStatus,
    ollamaInstallStatus,
    ollamaSettings,
    ollamaModels,
    ocrAnalysis,
    ocrProvisionStatus
  );
  const statusItems = issues.length > 0 ? detectSetupStatusItems(ocrProvisionStatus) : [];
  const items = [...issues, ...statusItems];

  if (items.length === 0 || dismissed) {
    return null;
  }

  const primaryIssue = items[0];

  return (
    <div className="setup-banner animate-fade-in" role="region" aria-label="Notifica di configurazione">
      <div className="setup-banner__content">
        <div className="setup-banner__icon">i</div>
        <div>
          <h3 className="setup-banner__title">
            {items.length === 1 ? primaryIssue.title : `Configurazione consigliata (${items.length} voci)`}
          </h3>
          <p className="setup-banner__desc">
            {primaryIssue.detail}
          </p>
          {primaryIssue.isRunning && (
            <p className="setup-banner__desc" style={{ marginTop: "4px", fontSize: "11px" }}>
              Aggiornamento automatico ogni 3 secondi...
            </p>
          )}
          {items.length > 1 && (
            <div className="setup-banner__subitems" style={{ marginTop: "6px" }}>
              {items.map((item) => (
                <div key={item.key} style={{ fontSize: "12px", opacity: 0.9 }}>
                  <span>{item.title}</span>: <span>{item.detail}</span>
                </div>
              ))}
            </div>
          )}
          {ocrProvisionStatus?.runtimeDetail && (
            <div style={{ fontSize: "11px", opacity: 0.8, marginTop: "4px" }}>
              {ocrProvisionStatus.runtimeDetail}
            </div>
          )}
          {ocrLastCheckedAt && (
            <div style={{ fontSize: "11px", opacity: 0.7, marginTop: "2px" }}>
              Ultima verifica: {formatSetupTime(ocrLastCheckedAt)}
            </div>
          )}
        </div>
      </div>
      <div className="setup-banner__actions">
        {items.map((item) => {
          if (item.action === "install-ollama") {
            return (
              <button key={item.key} type="button" onClick={onInstallOllama}>
                {item.actionLabel ?? "Installa Ollama"}
              </button>
            );
          }
          if (item.action === "configure-ocr") {
            return (
              <button key={item.key} type="button" onClick={() => onConfigureOcr(item.runtimeTarget)} disabled={isConfiguringOcr}>
                {isConfiguringOcr ? "Configurazione OCR..." : item.actionLabel ?? "Configura OCR"}
              </button>
            );
          }
          if (item.action === "cancel-ocr") {
            return (
              <button key={item.key} type="button" className="button-secondary" onClick={onCancelOcr}>
                Annulla OCR
              </button>
            );
          }
          return null;
        })}
        <button type="button" className="button-secondary" onClick={onOpenSettings}>
          Apri Impostazioni
        </button>
        <button type="button" className="button-secondary" onClick={onRecheck} disabled={isChecking}>
          {isChecking ? "Controllo..." : "Verifica"}
        </button>
        <button
          type="button"
          className="button-secondary"
          title="Ignora per ora"
          onClick={() => setDismissed(true)}
          style={{ padding: "6px 10px" }}
        >
          ✕
        </button>
      </div>
    </div>
  );
}
