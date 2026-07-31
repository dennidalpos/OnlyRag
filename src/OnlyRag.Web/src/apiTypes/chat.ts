export type ChatRequest = {
  message: string;
  model: string;
  useDocuments: boolean;
  selectedDocumentIds: number[];
  conversationId: string | null;
};

export type ChatSource = {
  documentId: number;
  documentName: string;
  pageStart: number | null;
  pageEnd: number | null;
  chunkId: number;
  snippet: string;
  score: number;
};

export type ChatNotice = {
  code: string;
  message: string;
};

export type ChatResponse = {
  conversationId: string;
  model: string;
  answer: string;
  usedDocuments: boolean;
  sources: ChatSource[];
  notices: ChatNotice[];
};

export type ChatStreamChunkEvent = {
  eventType: "meta" | "chunk" | "done" | "error";
  conversationId?: string | null;
  model?: string | null;
  content?: string | null;
  sources?: ChatSource[] | null;
  notices?: ChatNotice[] | null;
};
