import { describe, expect, it, vi } from "vitest";
import { clearExitContributor, initializeAppLifecycleBridge, setExitContributor } from "../src/appLifecycle";
import { mockApi } from "../src/test/apiMock";
import { createJob } from "../src/test/fixtures";

describe("appLifecycle bridge", () => {
  it("combines pending UI changes with active backend jobs", async () => {
    mockApi([
      {
        path: "/api/jobs?limit=200",
        response: [createJob({ status: "Running" }), createJob({ id: "job-2", status: "Completed" })]
      }
    ]);
    const prepareForExit = vi.fn(async () => {
      window.localStorage.setItem("onlyrag.chat.draft", "bozza salvata");
    });

    initializeAppLifecycleBridge();
    setExitContributor("chat", {
      label: "Chat",
      hasPendingChanges: true,
      hasActiveWork: false,
      prepareForExit
    });

    const state = await window.__ONLYRAG_APP__!.getExitState();
    expect(state.activeJobCount).toBe(1);
    expect(state.isActiveJobStateUnknown).toBe(false);
    expect(state.reasons).toContain("Chat: modifiche non salvate.");
    expect(state.reasons).toContain("Job locali attivi: 1.");

    await window.__ONLYRAG_APP__!.prepareForExit();
    expect(prepareForExit).toHaveBeenCalledOnce();
    expect(window.localStorage.getItem("onlyrag.chat.draft")).toBe("bozza salvata");

    clearExitContributor("chat");
  });

  it("keeps exit guarded when active job polling fails", async () => {
    mockApi([
      {
        path: "/api/jobs?limit=200",
        handler: async () => {
          throw new TypeError("network offline");
        }
      }
    ]);

    initializeAppLifecycleBridge();

    const state = await window.__ONLYRAG_APP__!.getExitState();

    expect(state.activeJobCount).toBe(0);
    expect(state.isActiveJobStateUnknown).toBe(true);
    expect(state.hasActiveWork).toBe(true);
    expect(state.reasons).toContain("Stato dei job locali non verificabile.");
  });
});
