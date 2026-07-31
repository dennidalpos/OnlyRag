import { describe, expect, it } from "vitest";
import { formatDateTime, formatLastRefresh, formatTime } from "../src/pollingStatus";

describe("pollingStatus formatting", () => {
  it("formats refresh timestamps with the Italian UI locale", () => {
    const value = "2026-05-21T12:00:00";

    expect(formatDateTime(value)).toBe(new Date(value).toLocaleString("it-IT"));
    expect(formatTime(value)).toBe(new Date(value).toLocaleTimeString("it-IT"));
    expect(formatLastRefresh(value)).toBe(formatDateTime(value));
  });
});
