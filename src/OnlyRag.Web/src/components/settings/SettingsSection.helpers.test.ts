import { describe, expect, it } from "vitest";
import { createOllamaSettings } from "../../test/fixtures";
import {
  buildEmbeddingRecommendations,
  buildIngestionSettingsPayload,
  buildNumCtxRecommendation,
  isNonLocalUrl,
  normalizeOllamaSettings
} from "./SettingsSection.helpers";

describe("SettingsSection helpers", () => {
  it("requires explicit trust only for non-local Ollama endpoints", () => {
    expect(isNonLocalUrl("http://localhost:11434")).toBe(false);
    expect(isNonLocalUrl("http://127.0.0.1:11434")).toBe(false);
    expect(isNonLocalUrl("http://192.168.1.50:11434")).toBe(true);

    expect(
      normalizeOllamaSettings(
        createOllamaSettings({
          ollamaBaseUrl: " http://localhost:11434 ",
          trustNonLocalEndpoint: true
        })
      ).trustNonLocalEndpoint
    ).toBe(false);

    expect(
      normalizeOllamaSettings(
        createOllamaSettings({
          ollamaBaseUrl: "http://192.168.1.50:11434",
          trustNonLocalEndpoint: true
        })
      ).trustNonLocalEndpoint
    ).toBe(true);
  });

  it("clamps ingestion and context recommendations to supported ranges", () => {
    expect(buildIngestionSettingsPayload({
      chunkSizeTokens: 80,
      overlapTokens: 500,
      archive: {
        maxFileCount: 0,
        maxTotalUncompressedBytes: 0,
        maxFileUncompressedBytes: 0,
        maxDirectoryDepth: -1
      }
    })).toEqual({
      chunkSizeTokens: 100,
      overlapTokens: 50,
      archive: {
        maxFileCount: 1,
        maxTotalUncompressedBytes: 1,
        maxFileUncompressedBytes: 1,
        maxDirectoryDepth: 0
      }
    });
    expect(buildEmbeddingRecommendations(8192)).toEqual({
      embeddingNumCtx: 8192,
      chunkMinimum: 800,
      chunkMaximum: 2850
    });
    expect(buildNumCtxRecommendation(0)).toBeNull();
  });
});
