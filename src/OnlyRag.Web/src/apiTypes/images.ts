export type ImageGenerationSettings = {
  selectedModelId: string;
  requestTimeoutSeconds: number;
  preferGpu: boolean;
};

export type ImageGenerationRuntimeStatus = {
  state: string;
  isReady: boolean;
  executionProvider: string;
  message: string;
  suggestion: string | null;
  preferredExecutionProvider: string;
  modelState: string;
  fallbackReason: string | null;
};

export type ImageModelCatalogEntry = {
  id: string;
  displayName: string;
  recommendedProfile: string;
  downloadUrl: string;
  licenseLabel: string;
  expectedSizeBytes: number;
  requiredFiles: string[];
  sha256: string;
  isBuiltIn: boolean;
  modelType: string;
  modelProfile: string;
  supportedResolutions: string[];
  defaultSteps: number;
  defaultGuidance: number;
  scheduler: string;
  compatibilityNotes: string;
};

export type ImageModelCatalogEntryRequest = {
  id: string;
  displayName: string;
  recommendedProfile: string;
  downloadUrl: string;
  licenseLabel: string;
  expectedSizeBytes: number;
  requiredFiles: string[];
  sha256: string;
  modelType: string;
  modelProfile: string;
  supportedResolutions: string[];
  defaultSteps: number;
  defaultGuidance: number;
  scheduler: string;
  compatibilityNotes: string;
};

export type ImageModelLocalState = {
  modelId: string;
  state: string;
  isDownloaded: boolean;
  isVerified: boolean;
  localSizeBytes: number;
  localDirectory: string;
  verificationError: string | null;
  expectedSizeBytes: number;
  remainingDownloadBytes: number;
};

export type ImageModelDownloadResponse = {
  modelId: string;
  state: string;
  message: string;
};

export type ImageModelUrlVerificationRequest = {
  url: string;
};

export type ImageModelUrlVerificationResponse = {
  isValid: boolean;
  message: string;
  repositoryId: string | null;
  foundFiles: string[];
  missingFiles: string[];
  totalSizeBytes: number;
  suggestedDisplayName: string;
  suggestedRequiredFiles: string[];
};

export type ImageGenerationRequest = {
  prompt: string;
  negativePrompt: string | null;
  modelId: string | null;
  width: number;
  height: number;
  steps: number;
  batchSize: number;
  seed: number | null;
  guidanceScale: number | null;
};

export type ImageGenerationResponse = {
  provider: string;
  message: string;
  images: GeneratedImage[];
};

export type GeneratedImage = {
  id: number;
  provider: string;
  prompt: string;
  negativePrompt: string | null;
  model: string | null;
  width: number;
  height: number;
  steps: number;
  batchSize: number;
  seed: number | null;
  fileName: string;
  mimeType: string;
  fileSizeBytes: number;
  createdAtUtc: string;
};

export type ImagePromptTranslationResponse = {
  originalPrompt: string;
  translatedPrompt: string;
  targetLanguage: string;
  wasTranslated: boolean;
};
