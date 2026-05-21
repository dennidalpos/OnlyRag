import { useEffect, useState } from "react";
import { apiRequest, type JobStatus, type LocalJob } from "../api";
import { ProgressBar } from "./ProgressBar";

const statusLabels: Record<JobStatus, string> = {
  Pending: "In attesa",
  Running: "In esecuzione",
  Pausing: "Pausa in corso",
  Completed: "Completato",
  Failed: "Errore",
  Cancelled: "Annullato",
  Paused: "In pausa"
};

const typeLabels: Record<string, string> = {
  "document-ingestion": "Importazione documento",
  "document-embedding": "Indicizzazione documento",
  "document-translation": "Traduzione documento"
};

function formatJobType(type: string): string {
  return typeLabels[type] ?? type;
}

type JobsSectionProps = {
  onJobsChanged?: () => void;
};

export function JobsSection({ onJobsChanged }: JobsSectionProps) {
  const [jobs, setJobs] = useState<LocalJob[]>([]);
  const [error, setError] = useState<string | null>(null);

  async function refreshJobs() {
    try {
      const jobList = await apiRequest<LocalJob[]>("/api/jobs?limit=100");
      setJobs(jobList);
      setError(null);
    } catch {
      setError("Impossibile leggere la coda job locale.");
    }
  }

  useEffect(() => {
    let isCancelled = false;

    async function poll() {
      try {
        const jobList = await apiRequest<LocalJob[]>("/api/jobs?limit=100");
        if (!isCancelled) {
          setJobs(jobList);
          setError(null);
        }
      } catch {
        if (!isCancelled) {
          setError("Impossibile leggere la coda job locale.");
        }
      }
    }

    void poll();
    const interval = window.setInterval(() => void poll(), 3000);

    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, []);

  async function transitionJob(jobId: string, action: "cancel" | "pause" | "resume") {
    try {
      await apiRequest<LocalJob>(`/api/jobs/${jobId}/${action}`, {
        method: "POST",
        body: JSON.stringify({})
      });
      await refreshJobs();
      onJobsChanged?.();
    } catch {
      setError("Operazione non riuscita.");
    }
  }

  async function deleteJob(jobId: string) {
    try {
      await apiRequest<void>(`/api/jobs/${jobId}`, { method: "DELETE" });
      await refreshJobs();
      onJobsChanged?.();
    } catch {
      setError("Eliminazione non riuscita.");
    }
  }

  async function purgeCompleted() {
    try {
      await apiRequest<{ deleted: number }>("/api/jobs", { method: "DELETE" });
      await refreshJobs();
      onJobsChanged?.();
    } catch {
      setError("Pulizia non riuscita.");
    }
  }

  const hasTerminated = jobs.some(
    (j) => j.status === "Completed" || j.status === "Cancelled" || j.status === "Failed"
  );

  return (
    <div className="jobs-panel">
      <div className="jobs-toolbar">
        <h2>Operazioni</h2>
        <div className="settings-actions">
          {hasTerminated && (
            <button type="button" className="button-secondary" onClick={() => void purgeCompleted()}>
              Rimuovi completati
            </button>
          )}
          {error && <span className="jobs-error">{error}</span>}
        </div>
      </div>
      {jobs.length === 0 ? (
        <div className="empty-state">
          <p>Nessuna operazione in corso.</p>
        </div>
      ) : (
        <div className="jobs-list" aria-label="Operazioni">
          {jobs.map((job) => (
            <article className="job-row" key={job.id}>
              <div className="job-row__header">
                <div>
                  <strong>{formatJobType(job.type)}</strong>
                  <span>{job.currentStep || statusLabels[job.status]}</span>
                </div>
                <span className={`job-status job-status--${job.status.toLowerCase()}`}>
                  {statusLabels[job.status]}
                </span>
              </div>
              {(job.status === "Running" || job.status === "Pausing" || job.status === "Pending" || job.status === "Paused") && (
                <ProgressBar label={`Avanzamento ${job.progressPercent}%`} value={job.progressPercent} />
              )}
              <div className="job-row__meta">
                <span>{new Date(job.updatedAt).toLocaleString()}</span>
              </div>
              <div className="settings-actions">
                {(job.status === "Pending" || job.status === "Running") && (
                  <>
                    <button
                      type="button"
                      className="button-secondary"
                      onClick={() => void transitionJob(job.id, "pause")}
                    >
                      Pausa
                    </button>
                    <button
                      type="button"
                      className="button-danger"
                      onClick={() => void transitionJob(job.id, "cancel")}
                    >
                      Annulla
                    </button>
                  </>
                )}
                {job.status === "Pausing" && (
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => void transitionJob(job.id, "cancel")}
                  >
                    Annulla
                  </button>
                )}
                {(job.status === "Paused" || job.status === "Failed") && (
                  <button
                    type="button"
                    className="button-secondary"
                    onClick={() => void transitionJob(job.id, "resume")}
                  >
                    Riprendi
                  </button>
                )}
                {(job.status === "Completed" || job.status === "Cancelled" || job.status === "Failed") && (
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => void deleteJob(job.id)}
                  >
                    Elimina
                  </button>
                )}
              </div>
              {job.error && <p className="job-error-message">{job.error}</p>}
            </article>
          ))}
        </div>
      )}
    </div>
  );
}
