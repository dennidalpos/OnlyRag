import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { RagBenchmarkPanel } from "./RagBenchmarkPanel";
import * as apiClient from "../../apiClient";

vi.mock("../../apiClient", () => ({
  runRagBenchmark: vi.fn(),
  runConcurrencyBenchmark: vi.fn()
}));

describe("RagBenchmarkPanel", () => {
  it("renders benchmark launch buttons and title", () => {
    render(<RagBenchmarkPanel />);
    expect(screen.getByText("Benchmark & Osservabilità Retrieval RAG")).toBeInTheDocument();
    expect(screen.getByTitle("Metriche incluse")).toBeInTheDocument();
    expect(screen.getByText("Esegui Benchmark Ora")).toBeInTheDocument();
    expect(screen.getByText("Test Concorrenza & Rete")).toBeInTheDocument();
  });

  it("runs benchmark and renders latency metrics and P99 distribution", async () => {
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
            averageCragScore: 0.92,
            p99Ms: 75.0
          }
        }
      ],
      averageLatency: {
        queryEmbeddingMs: 12.5,
        qdrantSearchMs: 18.2,
        fts5SearchMs: 4.1,
        reRankingMs: 25.0,
        totalMs: 59.8,
        averageCragScore: 0.92,
        p99Ms: 75.0
      }
    };

    vi.mocked(apiClient.runRagBenchmark).mockResolvedValue(mockReport);

    render(<RagBenchmarkPanel />);
    const runBtn = screen.getByText("Esegui Benchmark Ora");
    fireEvent.click(runBtn);

    await waitFor(() => {
      expect(screen.getByText("59.8 ms")).toBeInTheDocument();
      expect(screen.getByText("bench_1")).toBeInTheDocument();
      expect(screen.getByText("test query")).toBeInTheDocument();
      expect(screen.getByText("92.0%")).toBeInTheDocument();
      expect(screen.getByText("P99: 75 ms")).toBeInTheDocument();
    });
  });

  it("runs concurrency benchmark and renders resilience metrics", async () => {
    const mockConcurrencyReport = {
      evaluatedAtUtc: "2026-08-03T12:00:00Z",
      concurrentClients: 10,
      totalRequests: 20,
      successfulRequests: 18,
      faultedRequests: 2,
      throughputRps: 45.5,
      averageLatencyMs: 110.2,
      p95LatencyMs: 180.0,
      p99LatencyMs: 210.5,
      faultToleranceRate: 0.9
    };

    vi.mocked(apiClient.runConcurrencyBenchmark).mockResolvedValue(mockConcurrencyReport);

    render(<RagBenchmarkPanel />);
    const concurBtn = screen.getByText("Test Concorrenza & Rete");
    fireEvent.click(concurBtn);

    await waitFor(() => {
      expect(screen.getByText("Report Resilienza & Alta Concorrenza")).toBeInTheDocument();
      expect(screen.getByText("45.5")).toBeInTheDocument();
      expect(screen.getByText("180")).toBeInTheDocument();
      expect(screen.getByText("210.5")).toBeInTheDocument();
      expect(screen.getByText("90.0%")).toBeInTheDocument();
    });
  });
});
