import type { ChatSource } from "../api";

export type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
  sources: ChatSource[];
};

export type PersistedChatSession = {
  conversationId: string | null;
  messages: ChatMessage[];
  selectedDocumentIds: number[];
};

export function loadChatSession(): PersistedChatSession | null {
  try {
    const raw = window.sessionStorage.getItem("onlyrag.chat.session");
    if (!raw) return null;
    return JSON.parse(raw) as PersistedChatSession;
  } catch {
    return null;
  }
}

export function saveChatSession(session: PersistedChatSession): void {
  try {
    window.sessionStorage.setItem("onlyrag.chat.session", JSON.stringify(session));
  } catch {
  }
}

export function clearChatSession(): void {
  try {
    window.sessionStorage.removeItem("onlyrag.chat.session");
  } catch {
  }
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

export function loadDocumentsPanelWidth(storageKey: string, min: number, max: number): number {
  try {
    const saved = Number(window.localStorage.getItem(storageKey));
    return Number.isFinite(saved) ? clamp(saved, min, max) : 220;
  } catch {
    return 220;
  }
}

export function getMaxDocumentsPanelWidth(
  layout: HTMLDivElement | null,
  minDocumentsPanelWidth: number,
  maxDocumentsPanelWidth: number,
  minChatPanelWidth: number
): number {
  if (!layout) {
    return maxDocumentsPanelWidth;
  }

  return Math.max(
    minDocumentsPanelWidth,
    Math.min(maxDocumentsPanelWidth, layout.getBoundingClientRect().width - minChatPanelWidth)
  );
}

export function formatPageRange(pageStart: number | null, pageEnd: number | null): string {
  if (!pageStart && !pageEnd) {
    return "Pagina non disponibile";
  }

  if (!pageEnd || pageStart === pageEnd) {
    return `Pagina ${pageStart}`;
  }

  return `Pagine ${pageStart}-${pageEnd}`;
}
