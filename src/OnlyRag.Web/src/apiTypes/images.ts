export type ImageGenerationSettings = {
  provider: string;
  automatic1111BaseUrl: string;
  comfyUiBaseUrl: string;
  requestTimeoutSeconds: number;
  trustNonLocalEndpoint: boolean;
  automatic1111Model: string | null;
  comfyUiWorkflowJson: string | null;
};

export type ImageGenerationProviderStatus = {
  provider: string;
  state: string;
  isReachable: boolean;
  baseUrl: string;
  message: string;
  suggestion: string | null;
};

export type ImageGenerationRequest = {
  provider: string;
  prompt: string;
  negativePrompt: string | null;
  model: string | null;
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

