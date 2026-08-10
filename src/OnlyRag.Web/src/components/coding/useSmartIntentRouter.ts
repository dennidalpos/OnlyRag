import { useMemo } from "react";
import type { CodingMode } from "./CodingSection.types";

export type DetectedIntentType = "inline_edit" | "simple_qa" | "multi_agent_task";

export type DetectedIntentMeta = {
  type: DetectedIntentType;
  label: string;
  badgeColor: string;
  description: string;
  recommendedOperatingMode: CodingMode;
};

type UseSmartIntentRouterProps = {
  promptInput: string;
  selectedWorkspaceFile?: string | null;
  attachedFileContent?: string | null;
};

const INLINE_EDIT_KEYWORDS = [
  "refattorizza", "modifica", "cambia", "fix", "correggi", "ottimizza",
  "aggiungi tipo", "converti", "rinomina", "pulisci", "edit", "update",
  "rewrite", "replace", "formatta", "inline", "sostituisci"
];

const MULTI_AGENT_KEYWORDS = [
  "crea modulo", "implementa", "scrivi test", "orchestra", "progetto",
  "nuovo modulo", "refactoring completo", "esegui comandi", "con test",
  "pipeline", "setup", "build", "architettura", "multi agent", "suite", "feature"
];

export function useSmartIntentRouter({
  promptInput,
  selectedWorkspaceFile,
  attachedFileContent
}: UseSmartIntentRouterProps): DetectedIntentMeta {
  return useMemo(() => {
    const text = promptInput.trim().toLowerCase();
    const hasAttachedFile = Boolean(selectedWorkspaceFile || attachedFileContent);

    // 1. Inline edit detection
    const isInlineKeyword = INLINE_EDIT_KEYWORDS.some((kw) => text.includes(kw));
    if (hasAttachedFile && (isInlineKeyword || text.length < 80)) {
      return {
        type: "inline_edit",
        label: "⚡ Smart Router: Editing Chirurgico",
        badgeColor: "#38bdf8",
        description: "Modalità chirurgica attiva per modificare ed applicare modifiche sul file allegato.",
        recommendedOperatingMode: "full"
      };
    }

    // 2. Multi-agent complex task detection
    const isMultiAgentKeyword = MULTI_AGENT_KEYWORDS.some((kw) => text.includes(kw));
    if (isMultiAgentKeyword || text.length > 180 || (text.includes(" e ") && text.includes("test"))) {
      return {
        type: "multi_agent_task",
        label: "🤖 Smart Router: Task Complesso (Multi-Agente)",
        badgeColor: "#a855f7",
        description: "Obiettivo complesso rilevato. L'agente eseguirà la pianificazione e l'orchestrazione automatica.",
        recommendedOperatingMode: "full"
      };
    }

    // 3. Simple Q&A / Explanation
    return {
      type: "simple_qa",
      label: "💡 Smart Router: Consultazione & Q&A",
      badgeColor: "#22c55e",
      description: "Modalità informativa e di analisi. L'agente risponderà senza effettuare azioni distruttive sul disco.",
      recommendedOperatingMode: "ask"
    };
  }, [promptInput, selectedWorkspaceFile, attachedFileContent]);
}
