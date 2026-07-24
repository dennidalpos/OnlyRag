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
        <div className="setup-banner__icon" aria-hidden="true">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <circle cx="12" cy="12" r="10" />
            <line x1="12" y1="16" x2="12" y2="12" />
            <line x1="12" y1="8" x2="12.01" y2="8" />
          </svg>
        </div>
        <div>
          <h2 className="setup-banner__title">
            {items.length === 1 ? primaryIssue.title : `Configurazione consigliata (${items.length} avvisi)`}
          </h2>
          <p className="setup-banner__desc">
            {primaryIssue.detail}
          </p>
          {primaryIssue.isRunning && (
            <p className="setup-banner__desc setup-banner__desc--muted">
              Aggiornamento automatico ogni 3 secondi...
            </p>
          )}
          {items.length > 1 && (
            <div className="setup-banner__subitems">
              {items.map((item) => (
                <div key={item.key} className="setup-banner__subitem">
                  <span>{item.title}</span>: <span>{item.detail}</span>
                </div>
              ))}
            </div>
          )}
          {ocrProvisionStatus?.runtimeDetail && (
            <p className="setup-banner__desc setup-banner__desc--muted">
              {ocrProvisionStatus.runtimeDetail}
            </p>
          )}
          {ocrLastCheckedAt && (
            <div className="sr-only">
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
          title="Ignora notifica"
          aria-label="Chiudi avviso"
          onClick={() => setDismissed(true)}
        >
          ✕
        </button>
      </div>
    </div>
  );
}
