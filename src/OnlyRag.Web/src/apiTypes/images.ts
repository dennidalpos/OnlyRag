export type ImageGenerationSettings = {
  selectedModelId: string;
  requestTimeoutSeconds: number;
  preferGpu: boolean;
  activeExecutionProvider: string;
};

export type ImageGenerationRuntimeStatus = {
  state: string;
  isReady: boolean;
  executionProvider: string;
  message: string;
  suggestion: string | null;
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
};

export type ImageModelLocalState = {
  modelId: string;
  state: string;
  isDownloaded: boolean;
  isVerified: boolean;
  localSizeBytes: number;
  localDirectory: string;
  verificationError: string | null;
};

export type ImageModelDownloadResponse = {
  modelId: string;
  state: string;
  message: string;
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
