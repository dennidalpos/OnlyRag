import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vitest";

beforeEach(() => {
  window.__ONLYRAG_BACKEND__ = {
    isRunning: true,
    baseUrl: "http://127.0.0.1:49152",
    apiToken: "test-token",
    apiTokenHeaderName: "X-OnlyRag-Test",
    errorMessage: null
  };
});

afterEach(() => {
  cleanup();
  window.localStorage.clear();
  window.sessionStorage.clear();
  delete window.__ONLYRAG_BACKEND__;
  vi.unstubAllGlobals();
  vi.useRealTimers();
});
