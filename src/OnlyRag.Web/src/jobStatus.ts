import type { JobStatus } from "./api";

const activeJobStatuses = new Set<JobStatus>(["Pending", "Running", "Pausing", "Paused"]);
const terminalJobStatuses = new Set<JobStatus>(["Completed", "Cancelled", "Failed"]);

export function isActiveJobStatus(status: JobStatus): boolean {
  return activeJobStatuses.has(status);
}

export function isTerminalJobStatus(status: JobStatus): boolean {
  return terminalJobStatuses.has(status);
}
