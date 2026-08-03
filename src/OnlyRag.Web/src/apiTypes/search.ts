import type { DocumentStatus } from "./documents";

export type DocumentSearchRequest = {
  query: string;
  documentIds: number[];
  topK?: number;
  collectionName?: string;
  tags?: string[];
};

export type DocumentSearchResult = {
  documentId: number;
  documentName: string;
  pageStart: number | null;
  pageEnd: number | null;
  chunkId: number;
  snippet: string;
  score: number;
  reRankScore: number | null;
  parentContent: string | null;
  queryVariant: string | null;
  sectionHeading: string | null;
  chunkLevel: string;
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
