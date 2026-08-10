export type ModelIntegrityIssue = {
  path: string;
  reason: string;
  diagnosticAction: "download" | string;
};

export type ModelIntegrityStatus = {
  isHealthy: boolean;
  issues: ModelIntegrityIssue[];
  requiresOnDemandRepair: boolean;
};

export type UpdateFailure = {
  path: string;
  error: string;
};

export type UpdateResult = {
  version: string;
  updatedFiles: string[];
  skippedFiles: string[];
  failedFiles: UpdateFailure[];
  modelIntegrity: ModelIntegrityStatus;
};

export type ApplyUpdateRequest = {
  releaseDirectory: string;
  manifestPath: string;
};
