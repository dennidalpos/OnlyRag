import { describe, expect, it, vi } from "vitest";
import { clearExitContributor, initializeAppLifecycleBridge, setExitContributor } from "./appLifecycle";
import { mockApi } from "./test/apiMock";
import { createJob } from "./test/fixtures";

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
    expect(state.reasons).toContain("Chat: modifiche non salvate.");
    expect(state.reasons).toContain("Job locali attivi: 1.");

    await window.__ONLYRAG_APP__!.prepareForExit();
    expect(prepareForExit).toHaveBeenCalledOnce();
    expect(window.localStorage.getItem("onlyrag.chat.draft")).toBe("bozza salvata");

    clearExitContributor("chat");
  });
});
