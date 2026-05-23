export type DiagnosticsResponse = {
  appVersion: string;
  databasePath: string;
  logsDirectory: string;
  ollamaStatus: string;
  ollamaIsReachable: boolean;
  ocrStatus: string;
  ocrIsConfigured: boolean;
  ocrEngineName: string;
  ocrGpuCapability: OcrGpuCapability;
  systemTelemetry: SystemTelemetry;
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
