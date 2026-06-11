import type { OllamaRunningModel } from "./settings";
import type { ImageGenerationRuntimeStatus } from "./images";

export type DiagnosticsResponse = {
  appVersion: string;
  databasePath: string;
  logsDirectory: string;
  ollamaStatus: string;
  ollamaIsReachable: boolean;
  ollamaVersion: string | null;
  ollamaRunningModels: OllamaRunningModel[] | null;
  qdrant: QdrantStatusResponse;
  ocrStatus: string;
  ocrIsConfigured: boolean;
  ocrEngineName: string;
  ocrGpuCapability: OcrGpuCapability;
  systemTelemetry: SystemTelemetry;
  imageGeneration: ImageGenerationRuntimeStatus | null;
};

export type QdrantStatusResponse = {
  status: string;
  isReachable: boolean;
  grpcEndpoint: string;
  isLoopback: boolean;
  isTls: boolean;
  hasApiKey: boolean;
  version: string | null;
  binaryPath: string | null;
  configPath: string | null;
  storageDirectory: string | null;
  processId: number | null;
  warning: string | null;
  error: string | null;
};

export type OcrGpuCapability = {
  isUsable: boolean;
  status: string;
  blockReason: string | null;
  runtimeDetail: string | null;
  engineVersion: string | null;
  nvidiaName: string | null;
  driverVersion: string | null;
  compiledWithCuda: boolean | null;
  cudaDeviceCount: number | null;
  activeDevice: string | null;
  packageVersions: Record<string, string>;
  capabilityStatus: string;
};

export type SystemTelemetry = {
  cpu: CpuTelemetry;
  memory: MemoryTelemetry;
  systemDisk: DiskTelemetry;
  gpu: GpuTelemetry | null;
};

export type CpuTelemetry = {
  logicalProcessorCount: number;
  usagePercent: number | null;
};

export type MemoryTelemetry = {
  totalBytes: number;
  availableBytes: number;
};

export type DiskTelemetry = {
  name: string;
  totalBytes: number;
  availableBytes: number;
};

export type GpuTelemetry = {
  name: string;
  driverVersion: string;
  usagePercent: number | null;
  memoryTotalBytes: number | null;
  memoryAvailableBytes: number | null;
};

export type VectorBackendHealth = {
  backendName: string;
  storagePersistent: boolean;
  vectorLimit: number;
  totalVectors: number;
  nearLimit: boolean;
  warning: string | null;
};
