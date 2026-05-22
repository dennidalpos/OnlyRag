import { describe, expect, it } from "vitest";
import { isActiveJobStatus, isTerminalJobStatus } from "./jobStatus";
import type { JobStatus } from "./api";

describe("job status helpers", () => {
  it("classifies active local job statuses consistently", () => {
    const active: JobStatus[] = ["Pending", "Running", "Pausing", "Paused"];
    const inactive: JobStatus[] = ["Completed", "Cancelled", "Failed"];

    expect(active.every(isActiveJobStatus)).toBe(true);
    expect(inactive.some(isActiveJobStatus)).toBe(false);
  });

  it("classifies terminal local job statuses consistently", () => {
    const terminal: JobStatus[] = ["Completed", "Cancelled", "Failed"];
    const nonTerminal: JobStatus[] = ["Pending", "Running", "Pausing", "Paused"];

    expect(terminal.every(isTerminalJobStatus)).toBe(true);
    expect(nonTerminal.some(isTerminalJobStatus)).toBe(false);
  });
});
