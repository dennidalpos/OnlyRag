import React, { useState } from "react";
import { Gauge, Play, Activity, Clock, ShieldCheck, Zap, Server } from "lucide-react";
import { runRagBenchmark } from "../../apiClient";
import type { RetrievalBenchmarkReport } from "../../apiTypes/search";
import { InfoTip } from "../common/InfoTip";

export const RagBenchmarkPanel: React.FC = () => {
  const [report, setReport] = useState<RetrievalBenchmarkReport | null>(null);
  const [isRunning, setIsRunning] = useState(false);
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

  const avgLatency = report?.averageLatency;

  return (
    <div className="space-y-6 rounded-xl border border-slate-700/50 bg-slate-900/60 p-6 backdrop-blur-md shadow-xl">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="flex items-center gap-2">
            <Activity className="h-6 w-6 text-indigo-400" />
            <h2 className="text-xl font-bold text-slate-100">Benchmark Retrieval RAG</h2>
            <InfoTip label="Metriche incluse">
              Misura le latenze di embedding, Qdrant HNSW, SQLite FTS5 e re-ranking, oltre al punteggio di confidenza CRAG.
            </InfoTip>
          </div>
        </div>

        <button
          type="button"
          onClick={handleRunBenchmark}
          disabled={isRunning}
          className="flex items-center gap-2 self-start sm:self-auto bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-500 hover:to-violet-500 text-white font-medium px-4 py-2 rounded-lg transition-all duration-200 shadow-md hover:shadow-indigo-500/25 disabled:opacity-50"
        >
          {isRunning ? (
            <>
              <span className="h-4 w-4 animate-spin rounded-full border-2 border-white border-t-transparent" />
              <span>Analisi in corso...</span>
            </>
          ) : (
            <>
              <Play className="h-4 w-4 fill-current" />
              <span>Esegui Benchmark Ora</span>
            </>
          )}
        </button>
      </div>

      {error && (
        <div className="rounded-lg border border-red-500/30 bg-red-950/40 p-4 text-sm text-red-300">
          {error}
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
                <span>Latenza Totale</span>
              </div>
              <div className="mt-2 text-2xl font-bold text-indigo-100">
                {avgLatency?.totalMs ?? 0} <span className="text-xs font-normal text-indigo-300">ms</span>
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

          {/* Detailed Test Cases Table */}
          <div className="overflow-hidden rounded-lg border border-slate-800 bg-slate-950/40">
            <div className="border-b border-slate-800 bg-slate-900/60 px-4 py-3 text-xs font-semibold text-slate-300">
              Dettaglio Caso di Test ({report.cases.length} valutati)
            </div>
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
        </div>
      ) : (
        <div className="rounded-lg border border-dashed border-slate-800 bg-slate-950/20 p-8 text-center text-slate-400">
          Nessun report disponibile.
        </div>
      )}
    </div>
  );
};
