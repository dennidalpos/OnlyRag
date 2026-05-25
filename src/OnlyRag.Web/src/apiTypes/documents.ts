export type DocumentStatus =
  | "Imported"
  | "Queued"
  | "Processing"
  | "Indexed"
  | "RequiresEmbeddingRebuild"
  | "RequiresAdditionalComponent"
  | "Failed";

export type ImportedDocument = {
  id: number;
  documentUid: string;
  originalFileName: string;
  originalPath: string;
  sha256: string | null;
  mimeType: string | null;
  fileExtension: string | null;
  fileSizeBytes: number;
  status: DocumentStatus;
  pageCount: number;
  chunkCount: number;
  currentJobId: string | null;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type DocumentEmbeddingStatus = {
  documentId: number;
  state: string;
  model: string | null;
  chunkCount: number;
  embeddedChunkCount: number;
  progressPercent: number;
  currentJobId: string | null;
  currentStep: string | null;
  vectorSearchBackend: string;
  lastEmbeddedAtUtc: string | null;
};

export type DocumentOcrStatus = {
  documentId: number;
  state: string;
  pageCount: number;
  ocrPageCount: number;
  currentPage: number;
  totalPages: number;
  averageConfidence: number | null;
  currentJobId: string | null;
  currentStep: string | null;
  engineName: string | null;
  lastError: string | null;
};

export type DocumentImportResult = {
  document: ImportedDocument;
  deduplicated: boolean;
  message: string;
};

export type DocumentImportFileResult = {
  fileName: string;
  document: ImportedDocument | null;
  deduplicated: boolean;
  succeeded: boolean;
  message: string;
  errorCode: string | null;
};

export type DocumentImportResponse = {
  documents: DocumentImportResult[];
  results: DocumentImportFileResult[];
  hasFailures: boolean;
};

export type OcrPolicy = "Auto" | "ForceAll";

export type PhaseState = "Todo" | "InProgress" | "Completed" | "Skipped" | "Failed" | "Obsolete";

export type PipelinePhaseInfo = {
  state: PhaseState;
  error: string | null;
  completedAtUtc: string | null;
};

export type DocumentPipelineStatus = {
  documentId: number;
  ocrPolicy: string;
  import: PipelinePhaseInfo;
  analysis: PipelinePhaseInfo;
  ocr: PipelinePhaseInfo;
  textExtraction: PipelinePhaseInfo;
  chunking: PipelinePhaseInfo;
  embedding: PipelinePhaseInfo;
  overallState: PhaseState;
  activeJobId: string | null;
  activeJobType: string | null;
};

export type DocumentPageInfo = {
  pageNumber: number;
  textContent: string | null;
  ocrStatus: string | null;
  ocrEngine: string | null;
  ocrConfidence: number | null;
  ocrError: string | null;
};

export type DocumentPreviewResponse = {
  documentId: number;
  originalFileName: string;
  mimeType: string | null;
  fileExtension: string | null;
  fileSizeBytes: number;
  pageCount: number;
  chunkCount: number;
  status: string;
  pageStart: number;
  pageSize: number;
  returnedPageCount: number;
  pages: DocumentPageInfo[];
};
