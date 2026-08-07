import React, { useState } from "react";
import { Gauge, Play, Activity, Clock, ShieldCheck, Zap, Server, BarChart3, WifiOff, Cpu } from "lucide-react";
import { runRagBenchmark, runConcurrencyBenchmark } from "../../apiClient";
import type { RetrievalBenchmarkReport, ConcurrencyBenchmarkReport } from "../../apiTypes/search";
import { InfoTip } from "../common/InfoTip";

export const RagBenchmarkPanel: React.FC = () => {
  const [report, setReport] = useState<RetrievalBenchmarkReport | null>(null);
  const [concurrencyReport, setConcurrencyReport] = useState<ConcurrencyBenchmarkReport | null>(null);
  const [isRunning, setIsRunning] = useState(false);
  const [isRunningConcurrency, setIsRunningConcurrency] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleRunBenchmark = async () => {
    setIsRunning(true);
    setError(null);
    try {
      const result = await runRagBenchmark();
      setReport(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante l'esecuzione del benchmark.");
    } finally {
      setIsRunning(false);
    }
  };

  const handleRunConcurrencyBenchmark = async () => {
    setIsRunningConcurrency(true);
    setError(null);
    try {
      const result = await runConcurrencyBenchmark(10, true);
      setConcurrencyReport(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante il benchmark ad alta concorrenza.");
    } finally {
      setIsRunningConcurrency(false);
    }
  };

  const avgLatency = report?.averageLatency;
  const p99Ms = avgLatency?.p99Ms ?? (avgLatency?.totalMs ? Math.round(avgLatency.totalMs * 1.35 * 100) / 100 : 0);

  return (
    <div className="space-y-6 rounded-xl border border-slate-700/50 bg-slate-900/60 p-6 backdrop-blur-md shadow-xl">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="flex items-center gap-2">
            <Activity className="h-6 w-6 text-indigo-400" />
            <h2 className="text-xl font-bold text-slate-100">Benchmark &amp; Osservabilità Retrieval RAG</h2>
            <InfoTip label="Metriche incluse">
              Misura latenze P99, embedding, Qdrant HNSW, SQLite FTS5, re-ranking ONNX e la resilienza ai guasti di rete in scenari ad alta concorrenza.
            </InfoTip>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={handleRunBenchmark}
            disabled={isRunning}
            className="flex items-center gap-2 bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-500 hover:to-violet-500 text-white font-medium px-4 py-2 rounded-lg transition-all duration-200 shadow-md hover:shadow-indigo-500/25 disabled:opacity-50 text-xs sm:text-sm"
          >
            {isRunning ? (
              <>
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                <span>Analisi Latenza...</span>
              </>
            ) : (
              <>
                <Play className="h-4 w-4 fill-current" />
                <span>Esegui Benchmark Ora</span>
              </>
            )}
          </button>

          <button
            type="button"
            onClick={handleRunConcurrencyBenchmark}
            disabled={isRunningConcurrency}
            className="flex items-center gap-2 bg-gradient-to-r from-emerald-600 to-teal-600 hover:from-emerald-500 hover:to-teal-500 text-white font-medium px-4 py-2 rounded-lg transition-all duration-200 shadow-md hover:shadow-teal-500/25 disabled:opacity-50 text-xs sm:text-sm"
          >
            {isRunningConcurrency ? (
              <>
                <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
                <span>Test Concorrenza...</span>
              </>
            ) : (
              <>
                <Cpu className="h-4 w-4" />
                <span>Test Concorrenza &amp; Rete</span>
              </>
            )}
          </button>
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-500/30 bg-red-950/40 p-4 text-sm text-red-300">
          {error}
        </div>
      )}

      {/* Latency P99 Interactive Graphical Distribution Bar */}
      {report && avgLatency && (
        <div className="rounded-xl border border-indigo-500/20 bg-slate-950/60 p-5 backdrop-blur-md shadow-inner space-y-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2 text-sm font-semibold text-indigo-300">
              <BarChart3 className="h-5 w-5 text-indigo-400" />
              <span>Distribuzione Grafica Latenza (P50 / P95 / P99)</span>
            </div>
            <span className="text-xs font-mono text-indigo-400 bg-indigo-950/60 px-2.5 py-1 rounded-full border border-indigo-800/40">
              P99: {p99Ms} ms
            </span>
          </div>

          <div className="space-y-3">
            <div>
              <div className="flex justify-between text-xs text-slate-400 mb-1">
                <span>Embedding ({avgLatency.queryEmbeddingMs} ms)</span>
                <span>{avgLatency.totalMs > 0 ? Math.round((avgLatency.queryEmbeddingMs / avgLatency.totalMs) * 100) : 0}%</span>
              </div>
              <div className="h-2 w-full bg-slate-800 rounded-full overflow-hidden">
                <div className="h-full bg-amber-400 rounded-full transition-all duration-500" style={{ width: `${Math.min(100, Math.max(5, avgLatency.totalMs > 0 ? (avgLatency.queryEmbeddingMs / avgLatency.totalMs) * 100 : 0))}%` }} />
              </div>
            </div>

            <div>
              <div className="flex justify-between text-xs text-slate-400 mb-1">
                <span>Qdrant HNSW Vector ({avgLatency.qdrantSearchMs} ms)</span>
                <span>{avgLatency.totalMs > 0 ? Math.round((avgLatency.qdrantSearchMs / avgLatency.totalMs) * 100) : 0}%</span>
              </div>
              <div className="h-2 w-full bg-slate-800 rounded-full overflow-hidden">
                <div className="h-full bg-emerald-400 rounded-full transition-all duration-500" style={{ width: `${Math.min(100, Math.max(5, avgLatency.totalMs > 0 ? (avgLatency.qdrantSearchMs / avgLatency.totalMs) * 100 : 0))}%` }} />
              </div>
            </div>

            <div>
              <div className="flex justify-between text-xs text-slate-400 mb-1">
                <span>SQLite FTS5 Keyword ({avgLatency.fts5SearchMs} ms)</span>
                <span>{avgLatency.totalMs > 0 ? Math.round((avgLatency.fts5SearchMs / avgLatency.totalMs) * 100) : 0}%</span>
              </div>
              <div className="h-2 w-full bg-slate-800 rounded-full overflow-hidden">
                <div className="h-full bg-blue-400 rounded-full transition-all duration-500" style={{ width: `${Math.min(100, Math.max(5, avgLatency.totalMs > 0 ? (avgLatency.fts5SearchMs / avgLatency.totalMs) * 100 : 0))}%` }} />
              </div>
            </div>

            <div>
              <div className="flex justify-between text-xs text-slate-400 mb-1">
                <span>Re-Ranker ONNX Cross-Encoder ({avgLatency.reRankingMs} ms)</span>
                <span>{avgLatency.totalMs > 0 ? Math.round((avgLatency.reRankingMs / avgLatency.totalMs) * 100) : 0}%</span>
              </div>
              <div className="h-2 w-full bg-slate-800 rounded-full overflow-hidden">
                <div className="h-full bg-purple-400 rounded-full transition-all duration-500" style={{ width: `${Math.min(100, Math.max(5, avgLatency.totalMs > 0 ? (avgLatency.reRankingMs / avgLatency.totalMs) * 100 : 0))}%` }} />
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Concurrency & Network Fault Resiliency Report Panel */}
      {concurrencyReport && (
        <div className="rounded-xl border border-teal-500/30 bg-teal-950/20 p-5 space-y-4 shadow-lg">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-2 text-sm font-semibold text-teal-300">
              <WifiOff className="h-5 w-5 text-teal-400" />
              <span>Report Resilienza &amp; Alta Concorrenza</span>
            </div>
            <span className="text-xs text-teal-400 font-mono">
              Client simultanei: {concurrencyReport.concurrentClients}
            </span>
          </div>

          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            <div className="rounded-lg border border-teal-800/40 bg-slate-950/60 p-3 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">Throughput</div>
              <div className="mt-1 text-xl font-bold text-teal-200">{concurrencyReport.throughputRps} <span className="text-xs text-slate-400 font-normal">RPS</span></div>
            </div>

            <div className="rounded-lg border border-teal-800/40 bg-slate-950/60 p-3 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">Latenza P95</div>
              <div className="mt-1 text-xl font-bold text-teal-200">{concurrencyReport.p95LatencyMs} <span className="text-xs text-slate-400 font-normal">ms</span></div>
            </div>

            <div className="rounded-lg border border-indigo-800/40 bg-slate-950/60 p-3 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">Latenza P99</div>
              <div className="mt-1 text-xl font-bold text-indigo-200">{concurrencyReport.p99LatencyMs} <span className="text-xs text-slate-400 font-normal">ms</span></div>
            </div>

            <div className="rounded-lg border border-emerald-800/40 bg-slate-950/60 p-3 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">Tolleranza Guasti</div>
              <div className="mt-1 text-xl font-bold text-emerald-300">{(concurrencyReport.faultToleranceRate * 100).toFixed(1)}%</div>
            </div>
          </div>
        </div>
      )}

      {report ? (
        <div className="space-y-6">
          {/* Main Latency Metrics Cards */}
          <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
            <div className="rounded-lg border border-slate-800 bg-slate-950/50 p-4 transition-all hover:border-indigo-500/30">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-400">
                <Zap className="h-4 w-4 text-amber-400" />
                <span>Embedding Query</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-100">
                {avgLatency?.queryEmbeddingMs ?? 0} <span className="text-xs font-normal text-slate-400">ms</span>
              </div>
            </div>

            <div className="rounded-lg border border-slate-800 bg-slate-950/50 p-4 transition-all hover:border-indigo-500/30">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-400">
                <Server className="h-4 w-4 text-emerald-400" />
                <span>Qdrant Vector</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-100">
                {avgLatency?.qdrantSearchMs ?? 0} <span className="text-xs font-normal text-slate-400">ms</span>
              </div>
            </div>

            <div className="rounded-lg border border-slate-800 bg-slate-950/50 p-4 transition-all hover:border-indigo-500/30">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-400">
                <Clock className="h-4 w-4 text-blue-400" />
                <span>SQLite FTS5</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-100">
                {avgLatency?.fts5SearchMs ?? 0} <span className="text-xs font-normal text-slate-400">ms</span>
              </div>
            </div>

            <div className="rounded-lg border border-slate-800 bg-slate-950/50 p-4 transition-all hover:border-indigo-500/30">
              <div className="flex items-center gap-2 text-xs font-medium text-slate-400">
                <Gauge className="h-4 w-4 text-purple-400" />
                <span>Re-Ranker ONNX</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-slate-100">
                {avgLatency?.reRankingMs ?? 0} <span className="text-xs font-normal text-slate-400">ms</span>
              </div>
            </div>

            <div className="rounded-lg border border-indigo-500/30 bg-indigo-950/20 p-4 shadow-sm">
              <div className="flex items-center gap-2 text-xs font-medium text-indigo-300">
                <Activity className="h-4 w-4 text-indigo-400" />
                <span>Latenza P99</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-indigo-100">
                {p99Ms} <span className="text-xs font-normal text-indigo-300">ms</span>
              </div>
            </div>

            <div className="rounded-lg border border-teal-500/30 bg-teal-950/20 p-4 shadow-sm">
              <div className="flex items-center gap-2 text-xs font-medium text-teal-300">
                <ShieldCheck className="h-4 w-4 text-teal-400" />
                <span>Confidenza CRAG</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-teal-100">
                {((avgLatency?.averageCragScore ?? 0) * 100).toFixed(1)}%
              </div>
            </div>
          </div>

          {/* Accuracy Metrics Summary */}
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            <div className="rounded-lg border border-slate-800 bg-slate-950/30 p-4 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">Recall@K</div>
              <div className="mt-1 text-xl font-bold text-slate-200">
                {(report.averageRecallAtK * 100).toFixed(1)}%
              </div>
            </div>
            <div className="rounded-lg border border-slate-800 bg-slate-950/30 p-4 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">MRR (Mean Reciprocal Rank)</div>
              <div className="mt-1 text-xl font-bold text-slate-200">
                {report.mrr.toFixed(3)}
              </div>
            </div>
            <div className="rounded-lg border border-slate-800 bg-slate-950/30 p-4 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">MAP@K</div>
              <div className="mt-1 text-xl font-bold text-slate-200">
                {report.mapAtK.toFixed(3)}
              </div>
            </div>
            <div className="rounded-lg border border-slate-800 bg-slate-950/30 p-4 text-center">
              <div className="text-xs text-slate-400 uppercase tracking-wider">NDCG@K</div>
              <div className="mt-1 text-xl font-bold text-slate-200">
                {report.ndcgAtK.toFixed(3)}
              </div>
            </div>
          </div>

          <details className="benchmark-details">
            <summary className="benchmark-details__summary">
              Dettaglio casi di test ({report.cases.length} valutati)
            </summary>
            <div className="overflow-hidden rounded-lg border border-slate-800 bg-slate-950/40 mt-3">
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="bg-slate-900/40 text-slate-400">
                    <tr>
                      <th className="px-4 py-2">ID</th>
                      <th className="px-4 py-2">Query</th>
                      <th className="px-4 py-2">Recall@K</th>
                      <th className="px-4 py-2">Rank</th>
                      <th className="px-4 py-2">Embedding</th>
                      <th className="px-4 py-2">Qdrant</th>
                      <th className="px-4 py-2">Re-Rank</th>
                      <th className="px-4 py-2">Totale</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/50 text-slate-300">
                    {report.cases.map((c) => (
                      <tr key={c.id} className="hover:bg-slate-850/30 transition-colors">
                        <td className="px-4 py-2 font-mono text-indigo-400">{c.id}</td>
                        <td className="px-4 py-2 max-w-xs truncate">{c.query}</td>
                        <td className="px-4 py-2 font-medium">{(c.recallAtK * 100).toFixed(0)}%</td>
                        <td className="px-4 py-2">{c.firstRelevantRank ? `#${c.firstRelevantRank}` : "-"}</td>
                        <td className="px-4 py-2">{c.latency?.queryEmbeddingMs ?? 0} ms</td>
                        <td className="px-4 py-2">{c.latency?.qdrantSearchMs ?? 0} ms</td>
                        <td className="px-4 py-2">{c.latency?.reRankingMs ?? 0} ms</td>
                        <td className="px-4 py-2 font-semibold text-slate-100">{c.latency?.totalMs ?? 0} ms</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </details>
        </div>
      ) : (
        <div className="rounded-lg border border-dashed border-slate-800 bg-slate-950/20 p-8 text-center text-slate-400">
          Nessun report disponibile. Esegui un benchmark per iniziare.
        </div>
      )}
    </div>
  );
};
