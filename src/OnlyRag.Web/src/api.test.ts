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
        response: {
          title: "Backend non disponibile",
          detail: "Ollama non raggiungibile.",
          status: 503,
          code: "ollama_unreachable"
        }
      }
    ]);

    await expect(apiRequest("/api/chat", { method: "POST", body: "{}" })).rejects.toThrow("Ollama non raggiungibile.");
  });

  it("falls back to the problem title for contract-shaped errors without details", async () => {
    mockApi([
      {
        path: "/api/documents/404",
        status: 404,
        response: {
          title: "Documento non trovato",
          status: 404,
          code: "not_found"
        }
      }
    ]);

    await expect(apiRequest("/api/documents/404", { retries: 0 })).rejects.toThrow("Documento non trovato");
  });

  it("retries on transient status codes like 404 and succeeds when subsequent attempt returns 200", async () => {
    let attempts = 0;
    mockApi([
      {
        path: "/api/transient-test",
        handler: () => {
          attempts++;
          if (attempts === 1) {
            return { status: 404, body: { title: "Not yet ready" } };
          }
          return { status: 200, body: { success: true } };
        }
      }
    ]);

    const result = await apiRequest<{ success: boolean }>("/api/transient-test", {
      retries: 2,
      retryDelayMs: 10
    });

    expect(result).toEqual({ success: true });
    expect(attempts).toBe(2);
  });

  it("exhausts retries and throws the final problem error if transient 404 persists", async () => {
    let attempts = 0;
    mockApi([
      {
        path: "/api/persistent-404",
        handler: () => {
          attempts++;
          return { status: 404, body: { detail: "Endpoint non trovato dopo retries" } };
        }
      }
    ]);

    await expect(
      apiRequest("/api/persistent-404", { retries: 2, retryDelayMs: 10 })
    ).rejects.toThrow("Endpoint non trovato dopo retries");

    expect(attempts).toBe(3);
  });

  it("tracks backend bridge online and offline state", () => {
    markBackendOffline();
    expect(window.__ONLYRAG_BACKEND__?.isRunning).toBe(false);

    markBackendOnline();
    expect(window.__ONLYRAG_BACKEND__?.isRunning).toBe(true);
  });
});
