import type { OcrProvisionStatus, OcrStartupAnalysis } from "../api";
import { ProgressBar } from "./ProgressBar";
import { formatTelemetryBytes } from "./SettingsSection.formatting";
import type { SetupIssue } from "./InitialSetupWizard.logic";
import { formatSetupDateTime, formatSetupTime } from "./InitialSetupWizard.logic";

type InitialSetupWizardIssueProps = {
  issue: SetupIssue;
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  ocrLastCheckedAt: Date | null;
  isChecking: boolean;
  isConfiguringOcr: boolean;
  onInstallOllama: () => void;
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void;
  onCancelOcr: () => void;
};

export function InitialSetupWizardIssue({
  issue,
  ocrAnalysis,
  ocrProvisionStatus,
  ocrLastCheckedAt,
  isChecking,
  isConfiguringOcr,
  onInstallOllama,
  onConfigureOcr,
  onCancelOcr
}: InitialSetupWizardIssueProps) {
  return (
    <article className={`setup-issue setup-issue--${issue.tone ?? "warning"}`}>
      <div>
        <div className="setup-issue__header">
          <h3>{issue.title}</h3>
          {issue.badge && <span className="status-chip status-chip--muted">{issue.badge}</span>}
        </div>
        <p>{issue.detail}</p>
        {issue.isRunning && (
          <div className="setup-issue__progress">
            <ProgressBar
              label={ocrProvisionStatus?.stepLabel ?? "Configurazione OCR in corso"}
              value={ocrProvisionStatus?.progressPercent ?? 0}
              indeterminate={!ocrProvisionStatus?.progressPercent}
            />
          </div>
        )}
        {issue.installCommand && <p>Pagina download: <code>{issue.installCommand}</code></p>}
        {issue.networkAccessHint && <p>{issue.networkAccessHint}</p>}
        {issue.key === "ocr" && ocrAnalysis && (
          <p>
            Spazio disponibile: {formatTelemetryBytes(ocrAnalysis.availableDiskBytes)} di{" "}
            {formatTelemetryBytes(ocrAnalysis.requiredDiskBytes)} richiesti.
          </p>
        )}
        {issue.key === "ocr" && ocrProvisionStatus?.runtimeDetail && (
          <p>{ocrProvisionStatus.runtimeDetail}</p>
        )}
        {issue.key === "ocr" && ocrProvisionStatus?.lastError && (
          <p>{ocrProvisionStatus.lastError}</p>
        )}
        {issue.key === "ocr" && ocrProvisionStatus?.updatedAtUtc && (
          <p className="setup-issue__meta">
            Fase aggiornata: {formatSetupDateTime(ocrProvisionStatus.updatedAtUtc)}.
          </p>
        )}
        {issue.key === "ocr" && (
          <p className="setup-issue__meta">
            Ultima verifica: {formatSetupTime(ocrLastCheckedAt)}.
            {issue.isRunning ? " Aggiornamento automatico ogni 3 secondi." : ""}
          </p>
        )}
      </div>
      {issue.action && (
        <button
          type="button"
          className={issue.action === "cancel-ocr" ? "button-danger" : undefined}
          onClick={() => handleIssueAction(issue, onInstallOllama, onConfigureOcr, onCancelOcr)}
          disabled={isChecking || isConfiguringOcr}
        >
          {issue.actionLabel}
        </button>
      )}
    </article>
  );
}

function handleIssueAction(
  issue: SetupIssue,
  onInstallOllama: () => void,
  onConfigureOcr: (runtimeTarget?: "auto" | "cpu" | "nvidia") => void,
  onCancelOcr: () => void
) {
  if (issue.action === "install-ollama") {
    onInstallOllama();
  } else if (issue.action === "configure-ocr") {
    onConfigureOcr(issue.runtimeTarget);
  } else {
    onCancelOcr();
  }
}
