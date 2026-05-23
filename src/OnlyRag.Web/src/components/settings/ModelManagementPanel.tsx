import {
  formatModelSize
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function ModelManagementPanel() {
  const {
    modelToInstall,
    setModelToInstall,
    installModel,
    isBusy,
    openOllamaModelLibrary,
    models,
    removeModel
  } = useSettingsSectionContext();

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
                disabled={isBusy || modelToInstall.trim().length === 0}
              >
                Installa
              </button>
              <button
                type="button"
                className="button-secondary"
                onClick={openOllamaModelLibrary}
              >
                Elenco modelli Ollama
              </button>
            </div>
            <div className="model-list" aria-label="Modelli installati">
              {models.length === 0 && (
                <div className="model-row model-row--empty">
                  <p>Nessun modello installato.</p>
                </div>
              )}
              {models.map((model) => (
                <div className="model-row" key={model.name}>
                  <div className="model-row__details">
                    <strong>{model.name}</strong>
                    <span>
                      {model.family ?? "Famiglia non indicata"} | {formatModelSize(model.size)}
                    </span>
                  </div>
                  <div className="model-row__actions">
                    <button
                      type="button"
                      className="button-danger model-row__remove"
                      onClick={() => void removeModel(model.name)}
                      disabled={isBusy}
                    >
                      Rimuovi
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
  );
}

