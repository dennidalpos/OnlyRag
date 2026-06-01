export type JobStatus = "Pending" | "Running" | "Pausing" | "Completed" | "Failed" | "Cancelled" | "Paused";

export type LocalJob = {
  id: string;
  type: string;
  status: JobStatus;
  priority: number;
  progressPercent: number;
  currentStep: string;
  payloadJson: string;
  checkpointJson: string;
  error: string | null;
  retryCount: number;
  maxRetries: number;
  nextAttemptAtUtc: string | null;
  createdAt: string;
  updatedAt: string;
};
