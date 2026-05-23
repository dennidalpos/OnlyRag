import { describe, expect, it } from "vitest";
import {
  clearCompareDraft,
  loadCompareDraft,
  saveOrClearCompareDraft
} from "./TranslationSection.storage";

describe("TranslationSection storage", () => {
  it("bounds translation compare drafts and clears invalid stored payloads", () => {
    const key = "onlyrag.translation.1.unit.10.draft";
    window.localStorage.setItem(key, "x".repeat(120_001));

    expect(loadCompareDraft(key)).toBeNull();
    expect(window.localStorage.getItem(key)).toBeNull();

    saveOrClearCompareDraft(key, true, "Corrected translation");
    expect(loadCompareDraft(key)).toBe("Corrected translation");

    clearCompareDraft(key);
    expect(window.localStorage.getItem(key)).toBeNull();
  });
});
