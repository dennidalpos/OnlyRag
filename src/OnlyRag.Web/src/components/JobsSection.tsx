import { useEffect, useState } from "react";
import { apiRequest, type JobStatus, type LocalJob } from "../api";
import { isActiveJobStatus, isTerminalJobStatus } from "../jobStatus";
import {
  formatDateTime,
  formatLastRefresh,
  initialRefreshStatus,
  markRefreshFailure,
  markRefreshSuccess,
  shouldSurfaceRefreshFailure
} from "../pollingStatus";
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

const jobsRefreshErrorMessage = "Impossibile leggere la coda job locale.";
const jobsRefreshIntervalMs = 3000;

function formatJobType(type: string): string {
  return typeLabels[type] ?? type;
}

function formatJobActionLabel(action: string, job: LocalJob): string {
  const jobLabel = `${formatJobType(job.type)} ${job.id}`;
  return `${action} ${jobLabel}`;
}

type JobsSectionProps = {
  onJobsChanged?: () => void;
};

export function JobsSection({ onJobsChanged }: JobsSectionProps) {
  const [jobs, setJobs] = useState<LocalJob[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [refreshStatus, setRefreshStatus] = useState(initialRefreshStatus);

  function applyJobsRefreshSuccess(jobList: LocalJob[]) {
    setJobs(jobList);
    setError(null);
    setRefreshStatus(markRefreshSuccess());
  }

  function applyJobsRefreshFailure(message: string) {
    setError(message);
    setRefreshStatus((current) => markRefreshFailure(current, message));
  }

  async function refreshJobs() {
    try {
      const jobList = await apiRequest<LocalJob[]>("/api/jobs?limit=100");
      applyJobsRefreshSuccess(jobList);
    } catch {
      applyJobsRefreshFailure(jobsRefreshErrorMessage);
    }
  }

  useEffect(() => {
    let isCancelled = false;

    async function poll() {
      try {
        const jobList = await apiRequest<LocalJob[]>("/api/jobs?limit=100");
        if (!isCancelled) {
          applyJobsRefreshSuccess(jobList);
        }
      } catch {
        if (!isCancelled) {
          applyJobsRefreshFailure(jobsRefreshErrorMessage);
        }
      }
    }

    void poll();
    const interval = window.setInterval(() => void poll(), jobsRefreshIntervalMs);

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

  const hasTerminated = jobs.some((job) => isTerminalJobStatus(job.status));

  return (
    <div className="jobs-panel">
      <div className="jobs-toolbar">
        <h2>Operazioni</h2>
        <div className="settings-actions">
          <span className="jobs-refresh-state">
            Ultimo aggiornamento: {formatLastRefresh(refreshStatus.lastSuccessfulRefreshAt)}
          </span>
          {hasTerminated && (
            <button type="button" className="button-secondary" onClick={() => void purgeCompleted()}>
              Rimuovi completati
            </button>
          )}
          {error && <span className="jobs-error" role="alert">{error}</span>}
        </div>
      </div>
      {shouldSurfaceRefreshFailure(refreshStatus) && (
        <div className="feedback-banner feedback-banner--error" role="alert">
          {refreshStatus.lastErrorMessage} Stato non aggiornato da {formatLastRefresh(refreshStatus.lastSuccessfulRefreshAt)}.
        </div>
      )}
      {jobs.length === 0 ? (
        <div className="empty-state" role="status">
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
              {isActiveJobStatus(job.status) && (
                <ProgressBar label={`Avanzamento ${job.progressPercent}%`} value={job.progressPercent} />
              )}
              <div className="job-row__meta">
                <span>{formatDateTime(job.updatedAt)}</span>
              </div>
              <div className="settings-actions">
                {(job.status === "Pending" || job.status === "Running") && (
                  <>
                    <button
                      type="button"
                      className="button-secondary"
                      aria-label={formatJobActionLabel("Metti in pausa", job)}
                      onClick={() => void transitionJob(job.id, "pause")}
                    >
                      Pausa
                    </button>
                    <button
                      type="button"
                      className="button-danger"
                      aria-label={formatJobActionLabel("Annulla", job)}
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
                    aria-label={formatJobActionLabel("Annulla", job)}
                    onClick={() => void transitionJob(job.id, "cancel")}
                  >
                    Annulla
                  </button>
                )}
                {(job.status === "Paused" || job.status === "Failed") && (
                  <button
                    type="button"
                    className="button-secondary"
                    aria-label={formatJobActionLabel("Riprendi", job)}
                    onClick={() => void transitionJob(job.id, "resume")}
                  >
                    Riprendi
                  </button>
                )}
                {isTerminalJobStatus(job.status) && (
                  <button
                    type="button"
                    className="button-danger"
                    aria-label={formatJobActionLabel("Elimina", job)}
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
