import { describe, expect, it } from "vitest";
import { apiRequest, markBackendOffline, markBackendOnline } from "./api";
import { mockApi } from "./test/apiMock";

describe("apiRequest", () => {
  it("adds the local session token and parses successful JSON responses", async () => {
    const api = mockApi([{ path: "/api/documents", response: [{ id: 1 }] }]);

    await expect(apiRequest("/api/documents")).resolves.toEqual([{ id: 1 }]);

    expect(api.calls).toHaveLength(1);
    expect(api.calls[0].headers.get("X-OnlyRag-Test")).toBe("test-token");
    expect(api.calls[0].headers.get("Content-Type")).toBe("application/json");
  });

  it("rejects paths that leave the backend origin or API namespace", async () => {
    mockApi([]);

    await expect(apiRequest("https://example.com/api/documents")).rejects.toThrow("Percorso API locale non valido.");
    await expect(apiRequest("/health")).rejects.toThrow("Percorso API locale non valido.");
  });

  it("surfaces problem details without exposing the raw response body", async () => {
    mockApi([
      {
        path: "/api/chat",
        method: "POST",
        status: 503,
        response: { title: "Backend non disponibile", detail: "Ollama non raggiungibile." }
      }
    ]);

    await expect(apiRequest("/api/chat", { method: "POST", body: "{}" })).rejects.toThrow("Ollama non raggiungibile.");
  });

  it("tracks backend bridge online and offline state", () => {
    markBackendOffline();
    expect(window.__ONLYRAG_BACKEND__?.isRunning).toBe(false);

    markBackendOnline();
    expect(window.__ONLYRAG_BACKEND__?.isRunning).toBe(true);
  });
});
