import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { RagBenchmarkPanel } from "./RagBenchmarkPanel";
import * as apiClient from "../../apiClient";

vi.mock("../../apiClient", () => ({
  runRagBenchmark: vi.fn()
}));

describe("RagBenchmarkPanel", () => {
  it("renders benchmark launch button", () => {
    render(<RagBenchmarkPanel />);
    expect(screen.getByText("Benchmarking Prestazioni Retrieval RAG")).toBeInTheDocument();
    expect(screen.getByText("Esegui Benchmark Ora")).toBeInTheDocument();
  });

  it("runs benchmark and renders latency metrics and case breakdown", async () => {
    const mockReport = {
      evaluatedAtUtc: "2026-08-03T12:00:00Z",
      defaultTopK: 5,
      caseCount: 1,
      averageRecallAtK: 1.0,
      mrr: 1.0,
      mapAtK: 1.0,
      ndcgAtK: 1.0,
      cases: [
        {
          id: "bench_1",
          query: "test query",
          topK: 5,
          expectedChunkIds: [1],
          returnedChunkIds: [1],
          hitChunkIds: [1],
          recallAtK: 1.0,
          reciprocalRank: 1.0,
          apAtK: 1.0,
          ndcgAtK: 1.0,
          firstRelevantRank: 1,
          latency: {
            queryEmbeddingMs: 12.5,
            qdrantSearchMs: 18.2,
            fts5SearchMs: 4.1,
            reRankingMs: 25.0,
            totalMs: 59.8,
            averageCragScore: 0.92
          }
        }
      ],
      averageLatency: {
        queryEmbeddingMs: 12.5,
        qdrantSearchMs: 18.2,
        fts5SearchMs: 4.1,
        reRankingMs: 25.0,
        totalMs: 59.8,
        averageCragScore: 0.92
      }
    };

    vi.mocked(apiClient.runRagBenchmark).mockResolvedValue(mockReport);

    render(<RagBenchmarkPanel />);
    const runBtn = screen.getByText("Esegui Benchmark Ora");
    fireEvent.click(runBtn);

    await waitFor(() => {
      expect(screen.getByText("59.8")).toBeInTheDocument();
      expect(screen.getByText("bench_1")).toBeInTheDocument();
      expect(screen.getByText("test query")).toBeInTheDocument();
      expect(screen.getByText("92.0%")).toBeInTheDocument();
    });
  });
});
