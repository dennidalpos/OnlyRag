export type JobStatus = "Pending" | "Running" | "Pausing" | "Completed" | "Failed" | "Cancelled" | "Paused";

export type LocalJob = {
  id: string;
  type: string;
  status: JobStatus;
  priority: number;
  progressPercent: number;
  currentStep: string;
  error: string | null;
  updatedAt: string;
};
