import type { ChatSource } from "../api";
import {
  readBoundedStorageItem,
  removeStorageItem,
  writeBoundedStorageItem
} from "../storage/webViewStorage";
import { clampNumber } from "../numberUtils";

const chatDraftStorageKey = "onlyrag.chat.draft";
const chatSessionStorageKey = "onlyrag.chat.session";
const documentsPanelWidthStorageKey = "onlyrag.chat.documentsPanelWidth";
const maxChatDraftCharacters = 16_000;
const maxChatSessionCharacters = 128_000;
const maxChatMessages = 80;
const maxMessageCharacters = 24_000;
const maxSelectedDocuments = 200;
const maxSourcesPerMessage = 20;
const maxSourceSnippetCharacters = 4_000;
const maxIdentifierCharacters = 256;

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
  selectedModel: string | null;
};

export const chatStorageKeys = {
  draft: chatDraftStorageKey,
  session: chatSessionStorageKey,
  documentsPanelWidth: documentsPanelWidthStorageKey
} as const;

export function loadChatDraft(): string {
  return readBoundedStorageItem(window.localStorage, chatDraftStorageKey, {
    maxCharacters: maxChatDraftCharacters
  }) ?? "";
}

export function saveOrClearChatDraft(value: string): void {
  if (value.trim().length === 0) {
    removeStorageItem(window.localStorage, chatDraftStorageKey);
    return;
  }

  writeBoundedStorageItem(window.localStorage, chatDraftStorageKey, value, {
    maxCharacters: maxChatDraftCharacters
  });
}

export function clearChatDraft(): void {
  removeStorageItem(window.localStorage, chatDraftStorageKey);
}

export function loadChatSession(): PersistedChatSession | null {
  const raw = readBoundedStorageItem(window.sessionStorage, chatSessionStorageKey, {
    maxCharacters: maxChatSessionCharacters
  });
  if (!raw) {
    return null;
  }

  try {
    const session = parseChatSession(JSON.parse(raw));
    if (!session) {
      clearChatSession();
      return null;
    }

    return session;
  } catch {
    clearChatSession();
    return null;
  }
}

export function saveChatSession(session: PersistedChatSession): void {
  const normalized = normalizeChatSession(session);
  if (!normalized) {
    clearChatSession();
    return;
  }

  writeBoundedStorageItem(window.sessionStorage, chatSessionStorageKey, JSON.stringify(normalized), {
    maxCharacters: maxChatSessionCharacters
  });
}

export function clearChatSession(): void {
  removeStorageItem(window.sessionStorage, chatSessionStorageKey);
}

export function loadDocumentsPanelWidth(min: number, max: number): number {
  const raw = readBoundedStorageItem(window.localStorage, documentsPanelWidthStorageKey, {
    maxCharacters: 16
  });
  const saved = raw === null ? NaN : Number(raw);
  return Number.isFinite(saved) ? clamp(saved, min, max) : 220;
}

export function saveDocumentsPanelWidth(width: number, min: number, max: number): void {
  writeBoundedStorageItem(
    window.localStorage,
    documentsPanelWidthStorageKey,
    String(clamp(width, min, max)),
    { maxCharacters: 16 }
  );
}

function parseChatSession(value: unknown): PersistedChatSession | null {
  if (!isRecord(value)) {
    return null;
  }

  const conversationId = parseNullableBoundedString(value.conversationId);
  const selectedModel = parseNullableBoundedString(value.selectedModel);
  const selectedDocumentIds = parseSelectedDocumentIds(value.selectedDocumentIds);
  const messages = parseMessages(value.messages);
  if (conversationId === undefined || selectedModel === undefined || !selectedDocumentIds || !messages) {
    return null;
  }

  return { conversationId, messages, selectedDocumentIds, selectedModel };
}

function normalizeChatSession(session: PersistedChatSession): PersistedChatSession | null {
  return parseChatSession({
    conversationId: session.conversationId,
    messages: session.messages.slice(-maxChatMessages),
    selectedDocumentIds: session.selectedDocumentIds.slice(0, maxSelectedDocuments),
    selectedModel: session.selectedModel
  });
}

function parseMessages(value: unknown): ChatMessage[] | null {
  if (!Array.isArray(value) || value.length > maxChatMessages) {
    return null;
  }

  const messages: ChatMessage[] = [];
  for (const item of value) {
    const message = parseMessage(item);
    if (!message) {
      return null;
    }

    messages.push(message);
  }

  return messages;
}

function parseMessage(value: unknown): ChatMessage | null {
  if (!isRecord(value)) {
    return null;
  }

  const id = parseBoundedString(value.id, maxIdentifierCharacters);
  const role = value.role === "user" || value.role === "assistant" ? value.role : null;
  const content = parseBoundedString(value.content, maxMessageCharacters);
  const sources = parseSources(value.sources);
  if (!id || !role || content === null || !sources) {
    return null;
  }

  return { id, role, content, sources };
}

function parseSources(value: unknown): ChatSource[] | null {
  if (!Array.isArray(value) || value.length > maxSourcesPerMessage) {
    return null;
  }

  const sources: ChatSource[] = [];
  for (const item of value) {
    if (!isRecord(item)) {
      return null;
    }

    const documentId = parseFiniteNumber(item.documentId);
    const documentName = parseBoundedString(item.documentName, maxIdentifierCharacters);
    const pageStart = parseNullableFiniteNumber(item.pageStart);
    const pageEnd = parseNullableFiniteNumber(item.pageEnd);
    const chunkId = parseFiniteNumber(item.chunkId);
    const snippet = parseBoundedString(item.snippet, maxSourceSnippetCharacters);
    const score = parseFiniteNumber(item.score);
    if (
      documentId === null ||
      !documentName ||
      pageStart === undefined ||
      pageEnd === undefined ||
      chunkId === null ||
      snippet === null ||
      score === null
    ) {
      return null;
    }

    sources.push({ documentId, documentName, pageStart, pageEnd, chunkId, snippet, score });
  }

  return sources;
}

function parseSelectedDocumentIds(value: unknown): number[] | null {
  if (!Array.isArray(value) || value.length > maxSelectedDocuments) {
    return null;
  }

  const ids = new Set<number>();
  for (const item of value) {
    const id = parseFiniteNumber(item);
    if (id === null || !Number.isInteger(id) || id <= 0) {
      return null;
    }

    ids.add(id);
  }

  return [...ids];
}

function parseNullableBoundedString(value: unknown): string | null | undefined {
  if (value === null || value === undefined) {
    return null;
  }

  return parseBoundedString(value, maxIdentifierCharacters) ?? undefined;
}

function parseBoundedString(value: unknown, maxLength: number): string | null {
  return typeof value === "string" && value.length <= maxLength ? value : null;
}

function parseFiniteNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function parseNullableFiniteNumber(value: unknown): number | null | undefined {
  return value === null || value === undefined ? null : parseFiniteNumber(value) ?? undefined;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function clamp(value: number, min: number, max: number): number {
  return clampNumber(value, min, max);
}
