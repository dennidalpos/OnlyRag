import { useRef } from "react";
import type {
  OcrProvisionStatus,
  OcrStartupAnalysis,
  OllamaInstallStatus,
  OllamaModel,
  OllamaSettings,
  OllamaStatusResponse
} from "../api";
import { ProgressBar } from "./ProgressBar";
import { formatTelemetryBytes } from "./SettingsSection.formatting";
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
  onConfigureOcr: () => void;
  onCancelOcr: () => void;
  onRecheck: () => void;
};

type SetupIssue = {
  key: string;
  title: string;
  detail: string;
  tone?: "warning" | "running" | "success";
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
  const statusItems = issues.length > 0 ? detectSetupStatusItems(ocrAnalysis, ocrProvisionStatus) : [];
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
            <article className={`setup-issue setup-issue--${issue.tone ?? "warning"}`} key={issue.key}>
              <div>
                <div className="setup-issue__header">
                  <h3>{issue.title}</h3>
                  {issue.badge && <span className="status-chip status-chip--muted">{issue.badge}</span>}
                </div>
                <p>{issue.detail}</p>
                {issue.isRunning && (
                  <div className="setup-issue__progress">
                    <ProgressBar label="Configurazione OCR in corso" value={0} indeterminate />
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
                    {issue.isRunning ? " Aggiornamento automatico ogni 5 secondi." : ""}
                  </p>
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

  issues.push(...detectOllamaIssues(ollamaStatus, ollamaInstallStatus, ollamaSettings, ollamaModels));

  const hasVerifiedOcrRuntime = Boolean(ocrProvisionStatus?.isConfigured && !ocrProvisionStatus.isRunning);
  if (ocrProvisionStatus?.isRunning) {
    issues.push({
      key: "ocr",
      title: "Configurazione OCR in corso",
      detail: ocrProvisionStatus.message,
      tone: "running",
      badge: ocrProvisionStatus.resolvedRuntime,
      action: "cancel-ocr",
      actionLabel: "Annulla OCR",
      isRunning: true
    });
  } else if (ocrProvisionStatus && isFailedOcrProvisionStatus(ocrProvisionStatus)) {
    issues.push({
      key: "ocr",
      title: ocrProvisionStatus.resolvedRuntime === "cancelled"
        ? "Configurazione OCR annullata"
        : "Configurazione OCR non completata",
      detail: ocrProvisionStatus.message,
      badge: ocrProvisionStatus.resolvedRuntime,
      action: "configure-ocr",
      actionLabel: "Riprova OCR"
    });
  } else if (ocrProvisionStatus && isRepairableOcrRuntimeStatus(ocrProvisionStatus)) {
    issues.push({
      key: "ocr",
      title: "Runtime OCR da riparare",
      detail: formatRepairableOcrRuntimeDetail(ocrProvisionStatus.message),
      badge: getKnownRuntimeBadge(ocrProvisionStatus.resolvedRuntime),
      action: "configure-ocr",
      actionLabel: "Ripara OCR"
    });
  } else if (ocrAnalysis?.shouldPrompt && !hasVerifiedOcrRuntime) {
    issues.push({
      key: "ocr",
      title: ocrAnalysis.title,
      detail: ocrProvisionStatus?.message ?? ocrAnalysis.message,
      badge: ocrAnalysis.recommendedRuntimeTarget === "nvidia" ? "NVIDIA GPU" : "CPU",
      action: "configure-ocr",
      actionLabel: "Configura OCR"
    });
  }

  return issues;
}

function detectSetupStatusItems(
  ocrAnalysis: OcrStartupAnalysis | null,
  ocrProvisionStatus: OcrProvisionStatus | null
): SetupIssue[] {
  if (!ocrProvisionStatus?.isConfigured) {
    return [];
  }

  return [
    {
      key: "ocr",
      title: "OCR configurato",
      detail: ocrProvisionStatus.message,
      tone: "success",
      badge: ocrProvisionStatus.resolvedRuntime
    }
  ];
}

function isFailedOcrProvisionStatus(status: OcrProvisionStatus): boolean {
  return Boolean(status.lastError)
    || status.resolvedRuntime === "cancelled"
    || status.message.startsWith("Configurazione OCR non completata");
}

function isRepairableOcrRuntimeStatus(status: OcrProvisionStatus): boolean {
  return status.message.startsWith("Runtime OCR locale incompleto o danneggiato.");
}

function getKnownRuntimeBadge(value: string | null | undefined): string | undefined {
  if (!value || value === "unknown") {
    return undefined;
  }

  return value;
}

function formatRepairableOcrRuntimeDetail(message: string): string {
  if (!message.startsWith("Runtime OCR locale incompleto o danneggiato.")) {
    return message;
  }

  return "Runtime OCR locale incompleto o danneggiato. Premi Ripara OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.";
}

function formatSetupTime(value: Date | null): string {
  if (!value) {
    return "non ancora eseguita";
  }

  return value.toLocaleTimeString("it-IT", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

function formatSetupDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString("it-IT", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

function detectOllamaIssues(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[]
): SetupIssue[] {
  if (ollamaInstallStatus && !ollamaInstallStatus.cliInstalled) {
    return [{
      key: "ollama",
      title: "Ollama non installato",
      detail: "Installa Ollama manualmente per usare chat, embedding e traduzione con modelli locali.",
      action: "install-ollama",
      actionLabel: "Apri download Ollama",
      installCommand: ollamaInstallStatus.installCommand,
      networkAccessHint: ollamaInstallStatus.networkAccessHint
    }];
  }

  if (!ollamaStatus || !ollamaStatus.isReachable) {
    return [{
      key: "ollama",
      title: "Ollama non raggiungibile",
      detail: "Avvia Ollama e verifica l'indirizzo nelle Impostazioni.",
      networkAccessHint: ollamaInstallStatus?.networkAccessHint ?? null
    }];
  }

  if (ollamaModels.length === 0) {
    return [{
      key: "models",
      title: "Nessun modello installato",
      detail:
        "Installa almeno un modello chat e un modello embedding in Ollama, poi selezionali nelle Impostazioni."
    }];
  }

  const modelNames = new Set(ollamaModels.map((model) => model.name));
  return [
    detectRequiredModelIssue({
      key: "chat-model",
      modelName: ollamaSettings?.defaultChatModel,
      modelNames,
      missingTitle: "Modello chat non configurato",
      missingDetail: "Seleziona un modello da usare per la chat nelle Impostazioni.",
      unavailableTitlePrefix: "Modello chat non disponibile"
    }),
    detectRequiredModelIssue({
      key: "embedding-model",
      modelName: ollamaSettings?.defaultEmbeddingModel,
      modelNames,
      missingTitle: "Modello embedding non configurato",
      missingDetail: "Seleziona un modello da usare per l'indicizzazione dei documenti nelle Impostazioni.",
      unavailableTitlePrefix: "Modello embedding non disponibile"
    }),
    detectRequiredModelIssue({
      key: "translation-model",
      modelName: ollamaSettings?.defaultTranslationModel,
      modelNames,
      missingTitle: "Modello traduzione non configurato",
      missingDetail: "Seleziona un modello da usare per la traduzione dei documenti nelle Impostazioni.",
      unavailableTitlePrefix: "Modello traduzione non disponibile"
    })
  ].filter((issue): issue is SetupIssue => issue !== null);
}

function detectRequiredModelIssue({
  key,
  modelName,
  modelNames,
  missingTitle,
  missingDetail,
  unavailableTitlePrefix
}: {
  key: string;
  modelName: string | null | undefined;
  modelNames: Set<string>;
  missingTitle: string;
  missingDetail: string;
  unavailableTitlePrefix: string;
}): SetupIssue | null {
  if (!modelName) {
    return {
      key,
      title: missingTitle,
      detail: missingDetail
    };
  }

  if (!modelNames.has(modelName)) {
    return {
      key,
      title: `${unavailableTitlePrefix}: ${modelName}`,
      detail: "Installa il modello configurato oppure seleziona un modello diverso nelle Impostazioni."
    };
  }

  return null;
}
