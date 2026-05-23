import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach, beforeEach, vi } from "vitest";

beforeEach(() => {
  installMemoryStorage("localStorage");
  installMemoryStorage("sessionStorage");

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

function installMemoryStorage(name: "localStorage" | "sessionStorage") {
  const store = new Map<string, string>();
  const storage: Storage = {
    get length() {
      return store.size;
    },
    clear() {
      store.clear();
    },
    getItem(key: string) {
      return store.get(key) ?? null;
    },
    key(index: number) {
      return Array.from(store.keys())[index] ?? null;
    },
    removeItem(key: string) {
      store.delete(key);
    },
    setItem(key: string, value: string) {
      store.set(key, value);
    }
  };

  Object.defineProperty(window, name, {
    configurable: true,
    value: storage
  });
}
