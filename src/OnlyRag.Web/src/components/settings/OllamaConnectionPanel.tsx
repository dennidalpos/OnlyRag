import { SettingsFieldLabel } from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OllamaConnectionPanel() {
  const {
    status,
    formState,
    setFormState,
    usesNonLocalOllamaEndpoint,
    hasDirtyOllamaSettings,
    saveSettings,
    isBusy,
    testConnection,
    ollamaInstallStatus,
    installOllama,
    loadError
  } = useSettingsSectionContext();
  const endpointTooltip = usesNonLocalOllamaEndpoint
    ? "Endpoint non locale: chat, embedding e traduzione inviano testo a questo servizio. Abilita la fiducia solo su reti attendibili."
    : "Indirizzo dell'API Ollama locale o remota da usare per chat, embedding e traduzione.";
  const connectionMessage = status?.suggestion ?? loadError ?? null;

  return (
        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Connessione Ollama</h3>
            <span className={`status-chip status-chip--${status?.isReachable ? "online" : "offline"}`}>
              {status?.isReachable ? "Online" : "Offline"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="ollama-url">
              <SettingsFieldLabel text="URL Ollama" tooltip={endpointTooltip} />
              <input
                id="ollama-url"
                type="url"
                value={formState.ollamaBaseUrl}
                title={endpointTooltip}
                aria-label="URL Ollama"
                onChange={(event) =>
                  setFormState((current) => ({ ...current, ollamaBaseUrl: event.target.value }))
                }
                placeholder="http://localhost:11434"
              />
            </label>
            {usesNonLocalOllamaEndpoint && (
              <label className="toggle-row" htmlFor="trust-non-local-ollama">
                <input
                  id="trust-non-local-ollama"
                  type="checkbox"
                  checked={formState.trustNonLocalEndpoint}
                  onChange={(event) =>
                    setFormState((current) => ({
                      ...current,
                      trustNonLocalEndpoint: event.target.checked
                    }))
                  }
                />
                <span title={endpointTooltip}>Considera attendibile questo endpoint Ollama non locale</span>
              </label>
            )}
            <div className="settings-actions">
              <button type="button" onClick={saveSettings} disabled={isBusy || !hasDirtyOllamaSettings}>
                Salva impostazioni
              </button>
              <button type="button" className="button-secondary" onClick={testConnection} disabled={isBusy}>
                Test connessione
              </button>
              {ollamaInstallStatus && !ollamaInstallStatus.cliInstalled && (
                <button
                  type="button"
                  className="button-secondary"
                  onClick={installOllama}
                  disabled={isBusy}
                  title={`Apre la pagina ufficiale per installare Ollama. Comando rilevato: ${ollamaInstallStatus.installCommand}`}
                >
                  Apri download Ollama
                </button>
              )}
            </div>
            {hasDirtyOllamaSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            {connectionMessage && (
              <div className="panel-note">
                <p>{connectionMessage}</p>
              </div>
            )}
          </div>
        </div>
  );
}


