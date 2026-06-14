import type { DocumentStatus } from "./documents";

export type DocumentSearchResult = {
  documentId: number;
  documentName: string;
  pageStart: number | null;
  pageEnd: number | null;
  chunkId: number;
  snippet: string;
  score: number;
};

export type DocumentSearchDocumentStatus = {
  documentId: number;
  documentName: string;
  status: DocumentStatus;
  isIndexed: boolean;
  embeddingState: string;
  chunkCount: number;
  embeddedChunkCount: number;
};

export type RetrievalNotice = {
  code: string;
  message: string;
};

export type DocumentSearchResponse = {
  results: DocumentSearchResult[];
  documents: DocumentSearchDocumentStatus[];
  keywordBackend: string;
  vectorBackend: string;
  maxContextCharacters: number;
  notices: RetrievalNotice[];
};
