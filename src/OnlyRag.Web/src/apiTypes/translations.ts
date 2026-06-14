export type CreateTranslationRequest = {
  documentId: number;
  targetLanguage: string;
  model: string;
};

export type TranslationSummary = {
  id: number;
  documentId: number;
  documentName: string;
  sourceLanguage: string;
  targetLanguage: string;
  model: string;
  status: string;
  jobId: string | null;
  unitCount: number;
  completedUnitCount: number;
  progressPercent: number;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TranslationUnit = {
  id: number;
  translationId: number;
  unitIndex: number;
  unitKind: string;
  displayLabel: string;
  pageNumber: number | null;
  sourceText: string;
  machineTranslatedText: string | null;
  translatedText: string | null;
  status: string;
  manuallyEdited: boolean;
  validationWarnings: string | null;
  error: string | null;
  attemptCount: number;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type TranslationDetail = {
  translation: TranslationSummary;
  units: TranslationUnit[];
};

export type TranslationCompare = {
  translation: TranslationSummary;
  currentPage: number;
  pagePosition: number;
  pageCount: number;
  previousPage: number | null;
  nextPage: number | null;
  units: TranslationUnit[];
};

export type TranslationExport = {
  translationId: number;
  format: string;
  outputPath: string;
  status: string;
};
