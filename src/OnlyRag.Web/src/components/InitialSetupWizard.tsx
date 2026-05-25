import { useRef } from "react";
import type {
  OcrProvisionStatus,
  OcrStartupAnalysis,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse
} from "../api";
import { InitialSetupWizardIssue } from "./InitialSetupWizardIssue";
import { detectSetupIssues, detectSetupStatusItems } from "./InitialSetupWizard.logic";
import { useModalFocusTrap } from "./useModalFocusTrap";

type InitialSetupWizardProps = {
  ollamaStatus: OllamaStatusResponse | null;
  ollamaInstallStatus: OllamaInstallStatus | null;
  ollamaSettings: OllamaSettings | null;
  ollamaModels: OllamaModel[];
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  ocrLastCheckedAt: Date | null;
  isChecking: boolean;
  isConfiguringOcr: boolean;
  onOpenSettings: () => void;
  onInstallOllama: () => void;
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void;
  onCancelOcr: () => void;
  onRecheck: () => void;
};

export function InitialSetupWizard({
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
}: InitialSetupWizardProps) {
  const modalRef = useRef<HTMLDivElement | null>(null);
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

  useModalFocusTrap(modalRef, items.length > 0);

  if (items.length === 0) {
    return null;
  }

  return (
    <div className="setup-gate-backdrop">
      <div
        className="setup-gate-modal setup-gate-modal--wide"
        role="dialog"
        aria-modal="true"
        aria-labelledby="setup-wizard-title"
        ref={modalRef}
        tabIndex={-1}
      >
        <div className="setup-wizard-header">
          <h2 id="setup-wizard-title">Configurazione iniziale richiesta</h2>
          <span className="status-chip status-chip--muted">{items.length} controlli</span>
        </div>
        <div className="setup-issue-list">
          {items.map((issue) => (
            <InitialSetupWizardIssue
              key={issue.key}
              issue={issue}
              ocrAnalysis={ocrAnalysis}
              ocrProvisionStatus={ocrProvisionStatus}
              ocrLastCheckedAt={ocrLastCheckedAt}
              isChecking={isChecking}
              isConfiguringOcr={isConfiguringOcr}
              onInstallOllama={onInstallOllama}
              onConfigureOcr={onConfigureOcr}
              onCancelOcr={onCancelOcr}
            />
          ))}
        </div>
        <div className="settings-actions">
          <button type="button" onClick={onOpenSettings}>
            Apri Impostazioni
          </button>
          <button type="button" className="button-secondary" onClick={onRecheck} disabled={isChecking}>
            {isChecking ? "Verifica in corso..." : "Verifica ora"}
          </button>
        </div>
      </div>
    </div>
  );
}
