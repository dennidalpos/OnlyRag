import type { DocumentStatus } from "./documents";

export type RagLatencyMetrics = {
  queryEmbeddingMs: number;
  qdrantSearchMs: number;
  fts5SearchMs: number;
  reRankingMs: number;
  totalMs: number;
  averageCragScore?: number;
  p99Ms?: number;
};

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
  latencyMetrics?: RagLatencyMetrics | null;
  notices: RetrievalNotice[];
};

export type RetrievalBenchmarkCaseResult = {
  id: string;
  query: string;
  topK: number;
  expectedChunkIds: number[];
  returnedChunkIds: number[];
  hitChunkIds: number[];
  recallAtK: number;
  reciprocalRank: number;
  apAtK: number;
  ndcgAtK: number;
  firstRelevantRank: number | null;
  latency?: RagLatencyMetrics | null;
};

export type RetrievalBenchmarkReport = {
  evaluatedAtUtc: string;
  defaultTopK: number;
  caseCount: number;
  averageRecallAtK: number;
  mrr: number;
  mapAtK: number;
  ndcgAtK: number;
  cases: RetrievalBenchmarkCaseResult[];
  averageLatency?: RagLatencyMetrics | null;
};

export type ConcurrencyBenchmarkReport = {
  evaluatedAtUtc: string;
  concurrentClients: number;
  totalRequests: number;
  successfulRequests: number;
  faultedRequests: number;
  throughputRps: number;
  averageLatencyMs: number;
  p95LatencyMs: number;
  p99LatencyMs: number;
  faultToleranceRate: number;
};
