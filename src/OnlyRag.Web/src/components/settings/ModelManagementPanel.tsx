import { apiRequest, type LocalJob } from "../../api";
import { ProgressBar } from "../common/ProgressBar";
import {
  formatModelSize
} from "./SettingsSection.helpers";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function ModelManagementPanel() {
  const {
    modelToInstall,
    setModelToInstall,
    installModel,
    isBusy,
    openOllamaModelLibrary,
    models,
    removeModel,
    modelPullJobs
  } = useSettingsSectionContext();
  const normalizedModelToInstall = modelToInstall.trim().toLowerCase();
  const activePullForInput = modelPullJobs.some((job) =>
    isActivePullJob(job) && getPullJobModelName(job).toLowerCase() === normalizedModelToInstall
  );

  async function transitionPullJob(jobId: string, action: "cancel" | "resume") {
    await apiRequest<LocalJob>(`/api/jobs/${jobId}/${action}`, {
      method: "POST",
      body: JSON.stringify({})
    });
  }

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Gestione modelli</h3>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="model-install">
              <span>Modello da installare</span>
              <input
                id="model-install"
                type="text"
                value={modelToInstall}
                onChange={(event) => setModelToInstall(event.target.value)}
                placeholder="es. gemma3:4b"
              />
            </label>
            <div className="settings-actions">
              <button
                type="button"
                onClick={installModel}
                disabled={isBusy || modelToInstall.trim().length === 0 || activePullForInput}
              >
                {activePullForInput ? "Installazione in corso" : "Installa"}
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={openOllamaModelLibrary}
              >
                Elenco modelli Ollama
              </button>
            </div>
            {modelPullJobs.length > 0 && (
              <div className="model-list" aria-label="Installazioni modelli">
                {modelPullJobs.map((job) => (
                  <div className="model-row" key={job.id}>
                    <div className="model-row__details">
                      <strong>{getPullJobModelName(job)}</strong>
                      <span>{job.currentStep || job.status}</span>
                      {isActivePullJob(job) && (
                        <ProgressBar label={`Installazione ${job.progressPercent}%`} value={job.progressPercent} />
                      )}
                      {job.error && <span className="job-error-message">{job.error}</span>}
                    </div>
                    <div className="model-row__actions">
                      {(job.status === "Pending" || job.status === "Running" || job.status === "Pausing") && (
                        <button
                          type="button"
                          className="button-secondary"
                          onClick={() => void transitionPullJob(job.id, "cancel")}
                          disabled={isBusy}
                        >
                          Annulla
                        </button>
                      )}
                      {(job.status === "Paused" || job.status === "Failed") && (
                        <button
                          type="button"
                          className="button-secondary"
                          onClick={() => void transitionPullJob(job.id, "resume")}
                          disabled={isBusy}
                        >
                          Riprova
                        </button>
                      )}
                    </div>
                  </div>
                ))}
              </div>
            )}
            <div className="model-list" aria-label="Modelli installati">
              {models.length === 0 && (
                <div className="model-row model-row--empty">
                  <p>Nessun modello installato.</p>
                </div>
              )}
              {models.map((model) => (
                <div className="model-row" key={model.name}>
                  <div className="model-row__details">
                    <strong title={model.name}>{model.name}</strong>
                    <span title={`${model.family ?? "Famiglia non indicata"} | ${formatModelSize(model.size)}`}>
                      {model.family ?? "Famiglia non indicata"} | {formatModelSize(model.size)}
                    </span>
                  </div>
                  <div className="model-row__actions">
                    <button
                      type="button"
                      className="button-danger model-row__remove icon-button"
                      onClick={() => void removeModel(model.name)}
                      disabled={isBusy}
                      aria-label={`Rimuovi modello ${model.name}`}
                      title={`Rimuovi ${model.name}`}
                    >
                      <svg aria-hidden="true" viewBox="0 0 24 24" focusable="false">
                        <path d="M9 3h6l1 2h4v2H4V5h4l1-2Zm1 6h2v9h-2V9Zm4 0h2v9h-2V9ZM7 9h2l1 11h4l1-11h2l-1 13H8L7 9Z" />
                      </svg>
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
  );
}

function isActivePullJob(job: LocalJob) {
  return job.status === "Pending" || job.status === "Running" || job.status === "Pausing" || job.status === "Paused";
}

function getPullJobModelName(job: LocalJob) {
  try {
    const payload = JSON.parse(job.payloadJson) as { modelName?: string; ModelName?: string };
    return payload.modelName?.trim() || payload.ModelName?.trim() || "Modello Ollama";
  } catch {
    return "Modello Ollama";
  }
}

