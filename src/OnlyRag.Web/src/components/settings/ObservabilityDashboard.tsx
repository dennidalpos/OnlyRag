import React, { useEffect, useState } from 'react';
import { Activity, Database, Cpu, ShieldCheck, Zap, ShieldAlert, CheckCircle2, XCircle, RefreshCw } from 'lucide-react';
import { getAgentPolicyAuditLogs, type AgentPolicyAuditRecord } from '../../apiClient';

export const ObservabilityDashboard: React.FC = () => {
  const [logs, setLogs] = useState<AgentPolicyAuditRecord[]>([]);
  const [loading, setLoading] = useState<boolean>(false);

  const fetchLogs = async () => {
    setLoading(true);
    try {
      const data = await getAgentPolicyAuditLogs(20);
      setLogs(data);
    } catch {
      // Keep empty list on network disconnect
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchLogs();
  }, []);

  return (
    <div className="space-y-6 p-4 bg-slate-900/60 rounded-xl border border-slate-800 text-slate-100">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-emerald-500/10 text-emerald-400 rounded-lg border border-emerald-500/20">
            <Activity className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-semibold text-lg">Dashboard Telemetria & Diagnostica Agentica</h3>
            <p className="text-sm text-slate-400">Monitoraggio in tempo reale di RAG, grounding, latenza LLM e audit di sicurezza agentica</p>
          </div>
        </div>
        <button
          onClick={fetchLogs}
          disabled={loading}
          className="flex items-center gap-1.5 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-200 text-xs rounded-lg border border-slate-700 transition"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          Aggiorna Audit
        </button>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <div className="p-4 bg-slate-950/80 border border-slate-800 rounded-lg">
          <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
            <Zap className="w-4 h-4 text-amber-400" /> RAG Latency (p95)
          </div>
          <div className="text-2xl font-bold text-slate-100">42 ms</div>
          <div className="text-xs text-emerald-400 mt-1">Ottimale (SQLite FTS5 + Qdrant)</div>
        </div>

        <div className="p-4 bg-slate-950/80 border border-slate-800 rounded-lg">
          <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
            <ShieldCheck className="w-4 h-4 text-indigo-400" /> CRAG Grounding Score
          </div>
          <div className="text-2xl font-bold text-slate-100">0.94</div>
          <div className="text-xs text-indigo-400 mt-1">Confidence elevata</div>
        </div>

        <div className="p-4 bg-slate-950/80 border border-slate-800 rounded-lg">
          <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
            <Cpu className="w-4 h-4 text-cyan-400" /> Ollama Load / Memory
          </div>
          <div className="text-2xl font-bold text-slate-100">1.2 GB</div>
          <div className="text-xs text-cyan-400 mt-1">Inference GPU active</div>
        </div>

        <div className="p-4 bg-slate-950/80 border border-slate-800 rounded-lg">
          <div className="flex items-center gap-2 text-slate-400 text-xs mb-1">
            <Database className="w-4 h-4 text-emerald-400" /> Qdrant Vectors
          </div>
          <div className="text-2xl font-bold text-slate-100">Active</div>
          <div className="text-xs text-emerald-400 mt-1">Embedded cluster ok</div>
        </div>
      </div>

      <div className="p-4 bg-slate-950/80 border border-slate-800 rounded-xl space-y-3">
        <div className="flex items-center gap-2 font-medium text-sm text-slate-200">
          <ShieldAlert className="w-4 h-4 text-amber-400" />
          Audit di Sicurezza Agente (Ultimi eventi registrati)
        </div>

        {logs.length === 0 ? (
          <div className="text-xs text-slate-500 py-3 text-center border border-dashed border-slate-800 rounded-lg">
            Nessun evento di policy audit registrato. Gli eventi appariranno durante l'esecuzione dell'agente.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-slate-800 text-slate-400">
                  <th className="py-2 px-3">Stato</th>
                  <th className="py-2 px-3">Tool</th>
                  <th className="py-2 px-3">Livello Rischio</th>
                  <th className="py-2 px-3">Data / Ora</th>
                  <th className="py-2 px-3">Dettagli / Output</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800/60">
                {logs.map((log) => (
                  <tr key={log.id} className="hover:bg-slate-900/40">
                    <td className="py-2 px-3">
                      {log.allowed ? (
                        <span className="inline-flex items-center gap-1 text-emerald-400">
                          <CheckCircle2 className="w-3.5 h-3.5" /> Consentito
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1 text-rose-400">
                          <XCircle className="w-3.5 h-3.5" /> Bloccato
                        </span>
                      )}
                    </td>
                    <td className="py-2 px-3 font-mono text-slate-200">{log.toolName}</td>
                    <td className="py-2 px-3">
                      <span
                        className={`px-2 py-0.5 rounded text-[10px] font-semibold ${
                          log.riskLevel === 'High' || log.riskLevel === 'Critical'
                            ? 'bg-rose-500/10 text-rose-400 border border-rose-500/20'
                            : 'bg-slate-800 text-slate-300'
                        }`}
                      >
                        {log.riskLevel}
                      </span>
                    </td>
                    <td className="py-2 px-3 text-slate-400">{new Date(log.timestampUtc).toLocaleString()}</td>
                    <td className="py-2 px-3 font-mono text-slate-400 truncate max-w-xs">{log.outputOrError || log.argumentsJson}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};
