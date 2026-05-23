import { useRef } from "react";
import type {
  OcrProvisionStatus,
  OcrStartupAnalysis,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse
} from "../api";
import { formatTelemetryBytes } from "./SettingsSection.formatting";
import { useModalFocusTrap } from "./useModalFocusTrap";

type InitialSetupWizardProps = {
  ollamaStatus: OllamaStatusResponse | null;
  ollamaInstallStatus: OllamaInstallStatus | null;
  ollamaSettings: OllamaSettings | null;
  ollamaModels: OllamaModel[];
  ocrAnalysis: OcrStartupAnalysis | null;
  ocrProvisionStatus: OcrProvisionStatus | null;
  isChecking: boolean;
  isConfiguringOcr: boolean;
  onOpenSettings: () => void;
  onInstallOllama: () => void;
  onConfigureOcr: () => void;
  onCancelOcr: () => void;
  onRecheck: () => void;
};

type SetupIssue = {
  key: string;
  title: string;
  detail: string;
  badge?: string;
  action?: "install-ollama" | "configure-ocr" | "cancel-ocr";
  actionLabel?: string;
  installCommand?: string | null;
  networkAccessHint?: string | null;
  isRunning?: boolean;
};

export function InitialSetupWizard({
  ollamaStatus,
  ollamaInstallStatus,
  ollamaSettings,
  ollamaModels,
  ocrAnalysis,
  ocrProvisionStatus,
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

  useModalFocusTrap(modalRef, issues.length > 0);

  if (issues.length === 0) {
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
          <span className="status-chip status-chip--muted">{issues.length} controlli</span>
        </div>
        <div className="setup-issue-list">
          {issues.map((issue) => (
            <article className="setup-issue" key={issue.key}>
              <div>
                <div className="setup-issue__header">
                  <h3>{issue.title}</h3>
                  {issue.badge && <span className="status-chip status-chip--muted">{issue.badge}</span>}
                </div>
                <p>{issue.detail}</p>
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
              </div>
              {issue.action && (
                <button
                  type="button"
                  className={issue.action === "cancel-ocr" ? "button-danger" : undefined}
                  onClick={() => {
                    if (issue.action === "install-ollama") {
                      onInstallOllama();
                    } else if (issue.action === "configure-ocr") {
                      onConfigureOcr();
                    } else {
                      onCancelOcr();
                    }
                  }}
                  disabled={isChecking || isConfiguringOcr}
                >
                  {issue.actionLabel}
                </button>
              )}
            </article>
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

function detectSetupIssues(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[],
  ocrAnalysis: OcrStartupAnalysis | null,
  ocrProvisionStatus: OcrProvisionStatus | null
): SetupIssue[] {
  const issues: SetupIssue[] = [];

  const ollamaIssue = detectOllamaIssue(ollamaStatus, ollamaInstallStatus, ollamaSettings, ollamaModels);
  if (ollamaIssue) {
    issues.push(ollamaIssue);
  }

  if (ocrProvisionStatus?.isRunning) {
    issues.push({
      key: "ocr",
      title: "Configurazione OCR in corso",
      detail: ocrProvisionStatus.message,
      badge: ocrProvisionStatus.resolvedRuntime,
      action: "cancel-ocr",
      actionLabel: "Annulla OCR",
      isRunning: true
    });
  } else if (ocrAnalysis?.shouldPrompt) {
    issues.push({
      key: "ocr",
      title: ocrAnalysis.title,
      detail: ocrAnalysis.message,
      badge: ocrAnalysis.recommendedRuntimeTarget === "nvidia" ? "NVIDIA GPU" : "CPU",
      action: "configure-ocr",
      actionLabel: "Configura OCR"
    });
  }

  return issues;
}

function detectOllamaIssue(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[]
): SetupIssue | null {
  if (ollamaInstallStatus && !ollamaInstallStatus.cliInstalled) {
    return {
      key: "ollama",
      title: "Ollama non installato",
      detail: "Installa Ollama manualmente per usare chat, embedding e traduzione con modelli locali.",
      action: "install-ollama",
      actionLabel: "Apri download Ollama",
      installCommand: ollamaInstallStatus.installCommand,
      networkAccessHint: ollamaInstallStatus.networkAccessHint
    };
  }

  if (!ollamaStatus || !ollamaStatus.isReachable) {
    return {
      key: "ollama",
      title: "Ollama non raggiungibile",
      detail: "Avvia Ollama e verifica l'indirizzo nelle Impostazioni.",
      networkAccessHint: ollamaInstallStatus?.networkAccessHint ?? null
    };
  }

  if (ollamaModels.length === 0) {
    return {
      key: "models",
      title: "Nessun modello installato",
      detail:
        "Installa almeno un modello chat e un modello embedding in Ollama, poi selezionali nelle Impostazioni."
    };
  }

  const modelNames = new Set(ollamaModels.map((model) => model.name));
  const chatModel = ollamaSettings?.defaultChatModel;
  if (!chatModel) {
    return {
      key: "chat-model",
      title: "Modello chat non configurato",
      detail: "Seleziona un modello da usare per la chat nelle Impostazioni."
    };
  }
  if (!modelNames.has(chatModel)) {
    return {
      key: "chat-model",
      title: `Modello chat non disponibile: ${chatModel}`,
      detail: "Installa il modello configurato oppure seleziona un modello diverso nelle Impostazioni."
    };
  }

  const embeddingModel = ollamaSettings?.defaultEmbeddingModel;
  if (!embeddingModel) {
    return {
      key: "embedding-model",
      title: "Modello embedding non configurato",
      detail: "Seleziona un modello da usare per l'indicizzazione dei documenti nelle Impostazioni."
    };
  }
  if (!modelNames.has(embeddingModel)) {
    return {
      key: "embedding-model",
      title: `Modello embedding non disponibile: ${embeddingModel}`,
      detail: "Installa il modello configurato oppure seleziona un modello diverso nelle Impostazioni."
    };
  }

  return null;
}
