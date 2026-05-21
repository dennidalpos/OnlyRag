import { useRef } from "react";
import type { OllamaInstallStatus, OllamaModel, OllamaSettings, OllamaStatusResponse } from "../api";
import { useModalFocusTrap } from "./useModalFocusTrap";

type OllamaSetupGateProps = {
  ollamaStatus: OllamaStatusResponse | null;
  ollamaInstallStatus: OllamaInstallStatus | null;
  ollamaSettings: OllamaSettings | null;
  ollamaModels: OllamaModel[];
  isChecking: boolean;
  onOpenSettings: () => void;
  onInstallOllama: () => void;
  onRecheck: () => void;
};

type SetupIssue = {
  title: string;
  detail: string;
  installCommand?: string | null;
  networkAccessHint?: string | null;
  canInstallOllama?: boolean;
};

function detectIssue(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaInstallStatus: OllamaInstallStatus | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[]
): SetupIssue | null {
  if (ollamaInstallStatus && !ollamaInstallStatus.cliInstalled) {
    return {
      title: "Ollama non installato",
      detail: "Installa Ollama manualmente per usare chat, embedding e traduzione con modelli locali.",
      installCommand: ollamaInstallStatus.installCommand,
      networkAccessHint: ollamaInstallStatus.networkAccessHint,
      canInstallOllama: true
    };
  }

  if (!ollamaStatus || !ollamaStatus.isReachable) {
    return {
      title: "Ollama non raggiungibile",
      detail:
        "L'app non riesce a connettersi a Ollama. Avvia Ollama, poi verifica l'indirizzo nelle Impostazioni.",
      networkAccessHint: ollamaInstallStatus?.networkAccessHint ?? null
    };
  }

  if (ollamaModels.length === 0) {
    return {
      title: "Nessun modello installato",
      detail:
        'Ollama è attivo ma non ha modelli installati. Installa almeno un modello per la chat e uno per l\'embedding con il comando "ollama pull <nome-modello>", poi configurali nelle Impostazioni.'
    };
  }

  const modelNames = new Set(ollamaModels.map((m) => m.name));

  const chatModel = ollamaSettings?.defaultChatModel;
  if (!chatModel) {
    return {
      title: "Modello chat non configurato",
      detail: "Seleziona un modello da usare per la chat nelle Impostazioni > Ollama."
    };
  }
  if (!modelNames.has(chatModel)) {
    return {
      title: `Modello chat non disponibile: ${chatModel}`,
      detail: `Il modello chat configurato "${chatModel}" non è installato in Ollama. Installalo oppure seleziona un modello diverso nelle Impostazioni.`
    };
  }

  const embeddingModel = ollamaSettings?.defaultEmbeddingModel;
  if (!embeddingModel) {
    return {
      title: "Modello embedding non configurato",
      detail: "Seleziona un modello da usare per l'indicizzazione dei documenti nelle Impostazioni > Ollama."
    };
  }
  if (!modelNames.has(embeddingModel)) {
    return {
      title: `Modello embedding non disponibile: ${embeddingModel}`,
      detail: `Il modello embedding configurato "${embeddingModel}" non è installato in Ollama. Installalo oppure seleziona un modello diverso nelle Impostazioni.`
    };
  }

  return null;
}

export function OllamaSetupGate({
  ollamaStatus,
  ollamaInstallStatus,
  ollamaSettings,
  ollamaModels,
  isChecking,
  onOpenSettings,
  onInstallOllama,
  onRecheck
}: OllamaSetupGateProps) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const issue = detectIssue(ollamaStatus, ollamaInstallStatus, ollamaSettings, ollamaModels);

  useModalFocusTrap(modalRef, Boolean(issue));

  if (!issue) {
    return null;
  }

  return (
    <div className="setup-gate-backdrop">
      <div
        className="setup-gate-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="setup-gate-title"
        ref={modalRef}
        tabIndex={-1}
      >
        <h2 id="setup-gate-title">Configurazione iniziale richiesta</h2>
        <h3>{issue.title}</h3>
        <p>{issue.detail}</p>
        {issue.installCommand && <p>Pagina download: <code>{issue.installCommand}</code></p>}
        {issue.networkAccessHint && <p>{issue.networkAccessHint}</p>}
        <div className="settings-actions">
          {issue.canInstallOllama && (
            <button type="button" onClick={onInstallOllama} disabled={isChecking}>
              Apri download Ollama
            </button>
          )}
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
