import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OllamaConnectionPanel() {
  const {
    status,
    formState,
    setFormState,
    usesNonLocalOllamaEndpoint,
    saveSettings,
    isBusy,
    testConnection,
    ollamaInstallStatus,
    installOllama,
    loadError
  } = useSettingsSectionContext();

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
              <span>URL Ollama</span>
              <input
                id="ollama-url"
                type="url"
                value={formState.ollamaBaseUrl}
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
                <span>Considera attendibile questo endpoint Ollama non locale</span>
              </label>
            )}
            <div className="settings-actions">
              <button type="button" onClick={saveSettings} disabled={isBusy}>
                Salva impostazioni
              </button>
              <button type="button" className="button-secondary" onClick={testConnection} disabled={isBusy}>
                Test connessione
              </button>
              {ollamaInstallStatus && !ollamaInstallStatus.cliInstalled && (
                <button type="button" className="button-secondary" onClick={installOllama} disabled={isBusy}>
                  Apri download Ollama
                </button>
              )}
            </div>
            <div className="panel-note">
              <p>{status?.message ?? loadError ?? "Configura l'indirizzo Ollama e testa la connessione."}</p>
              {status?.suggestion && <p>{status.suggestion}</p>}
              {ollamaInstallStatus && !ollamaInstallStatus.cliInstalled && (
                <p>Ollama non risulta installato. Il pulsante apre la pagina ufficiale: <code>{ollamaInstallStatus.installCommand}</code></p>
              )}
              {ollamaInstallStatus && (
                <p>{ollamaInstallStatus.networkAccessHint}</p>
              )}
              {usesNonLocalOllamaEndpoint && (
                <p>Chat, embedding e traduzione inviano testo all'endpoint configurato. Abilita la fiducia solo per un servizio Ollama che controlli su una rete attendibile.</p>
              )}
            </div>
          </div>
        </div>
  );
}


