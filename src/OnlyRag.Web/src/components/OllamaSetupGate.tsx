import { useRef } from "react";
import type { OllamaModel, OllamaSettings, OllamaStatusResponse } from "../api";
import { useModalFocusTrap } from "./useModalFocusTrap";

type OllamaSetupGateProps = {
  ollamaStatus: OllamaStatusResponse | null;
  ollamaSettings: OllamaSettings | null;
  ollamaModels: OllamaModel[];
  isChecking: boolean;
  onOpenSettings: () => void;
  onRecheck: () => void;
};

type SetupIssue = {
  title: string;
  detail: string;
};

function detectIssue(
  ollamaStatus: OllamaStatusResponse | null,
  ollamaSettings: OllamaSettings | null,
  ollamaModels: OllamaModel[]
): SetupIssue | null {
  if (!ollamaStatus || !ollamaStatus.isReachable) {
    return {
      title: "Ollama non raggiungibile",
      detail:
        "L'app non riesce a connettersi a Ollama. Assicurati che Ollama sia avviato, poi verifica l'indirizzo nelle Impostazioni."
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
  ollamaSettings,
  ollamaModels,
  isChecking,
  onOpenSettings,
  onRecheck
}: OllamaSetupGateProps) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const issue = detectIssue(ollamaStatus, ollamaSettings, ollamaModels);

  useModalFocusTrap(modalRef, Boolean(issue));

  if (!issue) {
    return null;
  }

  return (
    <div className="setup-gate-backdrop" role="dialog" aria-modal="true" aria-labelledby="setup-gate-title">
      <div className="setup-gate-modal" ref={modalRef} tabIndex={-1}>
        <h2 id="setup-gate-title">Configurazione iniziale richiesta</h2>
        <h3>{issue.title}</h3>
        <p>{issue.detail}</p>
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
