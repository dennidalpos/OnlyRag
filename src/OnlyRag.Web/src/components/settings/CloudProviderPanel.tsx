import { useState, useEffect } from "react";
import { resolveBackendBaseUrl } from "../../apiClient";
import { SettingsFieldLabel } from "./SettingsSection.helpers";

function getCloudLlmUrl(path: string): string {
  const baseUrl = resolveBackendBaseUrl();
  return baseUrl ? `${baseUrl.replace(/\/$/, "")}${path}` : path;
}

function getCloudLlmHeaders(hasBody = false): Record<string, string> {
  const bridge = window.__ONLYRAG_BACKEND__;
  const headers: Record<string, string> = {};
  if (hasBody) {
    headers["Content-Type"] = "application/json";
  }
  if (bridge?.apiToken && bridge.apiTokenHeaderName) {
    headers[bridge.apiTokenHeaderName] = bridge.apiToken;
  }
  return headers;
}

export enum CloudLlmProvider {
  OllamaLocal = 0,
  AzureOpenAi = 1,
  OpenAi = 2,
  Anthropic = 3,
  GoogleGemini = 4
}

export interface CloudLlmSettingsResponse {
  provider: CloudLlmProvider;
  endpoint: string;
  chatModel: string;
  embeddingModel: string;
  deploymentName: string;
  apiVersion: string;
  hasApiKey: boolean;
}

export interface CloudLlmTestResult {
  success: boolean;
  message: string;
  latencyMs: number;
}

export function CloudProviderPanel() {
  const [provider, setProvider] = useState<CloudLlmProvider>(CloudLlmProvider.OllamaLocal);
  const [endpoint, setEndpoint] = useState<string>("");
  const [chatModel, setChatModel] = useState<string>("");
  const [embeddingModel, setEmbeddingModel] = useState<string>("");
  const [deploymentName, setDeploymentName] = useState<string>("");
  const [apiVersion, setApiVersion] = useState<string>("2024-02-15-preview");
  const [apiKey, setApiKey] = useState<string>("");
  const [hasApiKey, setHasApiKey] = useState<boolean>(false);
  const [showApiKey, setShowApiKey] = useState<boolean>(false);

  const [loading, setLoading] = useState<boolean>(false);
  const [saving, setSaving] = useState<boolean>(false);
  const [testing, setTesting] = useState<boolean>(false);
  const [testResult, setTestResult] = useState<CloudLlmTestResult | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  useEffect(() => {
    fetchCloudSettings();
  }, []);

  const fetchCloudSettings = async () => {
    try {
      setLoading(true);
      const res = await fetch(getCloudLlmUrl("/api/settings/cloud-llm"), {
        headers: getCloudLlmHeaders()
      });
      if (res.ok) {
        const data: CloudLlmSettingsResponse = await res.json();
        setProvider((curr) => (curr !== data.provider ? data.provider : curr));
        setEndpoint((curr) => (curr !== data.endpoint ? data.endpoint : curr));
        setChatModel((curr) => (curr !== data.chatModel ? data.chatModel : curr));
        setEmbeddingModel((curr) => (curr !== data.embeddingModel ? data.embeddingModel : curr));
        setDeploymentName((curr) => (curr !== data.deploymentName ? data.deploymentName : curr));
        setApiVersion((curr) => {
          const next = data.apiVersion || "2024-02-15-preview";
          return curr !== next ? next : curr;
        });
        setHasApiKey((curr) => (curr !== data.hasApiKey ? data.hasApiKey : curr));
      }
    } catch {
      // Ignora errori di fetch iniziale se il server locale parte senza cloud
    } finally {
      setLoading(false);
    }
  };

  const handleSave = async () => {
    try {
      setSaving(true);
      setStatusMessage(null);
      const res = await fetch(getCloudLlmUrl("/api/settings/cloud-llm"), {
        method: "POST",
        headers: getCloudLlmHeaders(true),
        body: JSON.stringify({
          provider,
          endpoint,
          chatModel,
          embeddingModel,
          deploymentName,
          apiVersion,
          apiKey: apiKey || null
        })
      });
      if (res.ok) {
        const data: CloudLlmSettingsResponse = await res.json();
        setHasApiKey(data.hasApiKey);
        setApiKey("");
        setStatusMessage("Impostazioni Cloud LLM salvate con successo.");
      } else {
        let errorDetail = `Errore HTTP ${res.status}`;
        try {
          const errData = await res.json();
          if (errData?.message) errorDetail = errData.message;
        } catch {
          // ignora errore di parsing json
        }
        setStatusMessage(`Errore durante il salvataggio: ${errorDetail}`);
      }
    } catch {
      setStatusMessage("Errore di rete durante il salvataggio: verificare che il server locale OnlyRag sia in esecuzione.");
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    try {
      setTesting(true);
      setTestResult(null);
      const res = await fetch(getCloudLlmUrl("/api/settings/cloud-llm/test"), {
        method: "POST",
        headers: getCloudLlmHeaders(true),
        body: JSON.stringify({
          provider,
          endpoint,
          chatModel,
          embeddingModel,
          deploymentName,
          apiVersion,
          apiKey: apiKey || null
        })
      });
      if (res.ok) {
        const result: CloudLlmTestResult = await res.json();
        setTestResult(result);
      } else {
        let errorDetail = `Errore HTTP ${res.status}`;
        try {
          const errData = await res.json();
          if (errData?.message) errorDetail = errData.message;
        } catch {
          // ignora errore di parsing json
        }
        setTestResult({
          success: false,
          message: `Test del provider non riuscito: ${errorDetail}`,
          latencyMs: 0
        });
      }
    } catch {
      setTestResult({
        success: false,
        message: "Impossibile contattare il server locale per il test del provider. Verificare che il backend OnlyRag sia in esecuzione.",
        latencyMs: 0
      });
    } finally {
      setTesting(false);
    }
  };

  return (
    <div className="settings-card settings-card--wide">
      <div className="settings-card__header">
        <h3>Provider AI Remote Cloud & Hybrid (Microsoft.Extensions.AI)</h3>
        <span className={`status-chip status-chip--${provider === CloudLlmProvider.OllamaLocal ? "online" : "custom"}`}>
          {provider === CloudLlmProvider.OllamaLocal ? "Locale Ollama" : "Cloud Remote"}
        </span>
      </div>
      <div className="settings-form">
        <label className="field-group" htmlFor="cloud-provider-select">
          <SettingsFieldLabel text="Provider AI Selezionato" tooltip="Seleziona se utilizzare il runtime Ollama locale o un provider Cloud remoto." />
          <select
            id="cloud-provider-select"
            value={provider}
            disabled={loading || saving}
            onChange={(e) => setProvider(Number(e.target.value) as CloudLlmProvider)}
          >
            <option value={CloudLlmProvider.OllamaLocal}>Ollama Locale (Zero Cloud / Privacy Max)</option>
            <option value={CloudLlmProvider.AzureOpenAi}>Azure OpenAI Service</option>
            <option value={CloudLlmProvider.OpenAi}>OpenAI Cloud (GPT-4o, o3)</option>
            <option value={CloudLlmProvider.Anthropic}>Anthropic Claude (Claude 3.5 Sonnet)</option>
            <option value={CloudLlmProvider.GoogleGemini}>Google Gemini (Gemini 1.5 Flash / Pro)</option>
          </select>
        </label>

        {provider !== CloudLlmProvider.OllamaLocal && (
          <>
            <label className="field-group" htmlFor="cloud-endpoint">
              <SettingsFieldLabel text="Endpoint API Provider" tooltip="URL base dell'endpoint del provider cloud." />
              <input
                id="cloud-endpoint"
                type="url"
                value={endpoint}
                onChange={(e) => setEndpoint(e.target.value)}
                placeholder={
                  provider === CloudLlmProvider.AzureOpenAi
                    ? "https://my-resource.openai.azure.com"
                    : provider === CloudLlmProvider.OpenAi
                    ? "https://api.openai.com/v1"
                    : provider === CloudLlmProvider.Anthropic
                    ? "https://api.anthropic.com/v1"
                    : "https://generativelanguage.googleapis.com/v1beta"
                }
              />
            </label>

            <div className="field-row" style={{ display: "flex", gap: "1rem" }}>
              <label className="field-group" htmlFor="cloud-chat-model" style={{ flex: 1 }}>
                <SettingsFieldLabel text="Modello Chat LLM" tooltip="ID o nome del modello Chat." />
                <input
                  id="cloud-chat-model"
                  type="text"
                  value={chatModel}
                  onChange={(e) => setChatModel(e.target.value)}
                  placeholder={
                    provider === CloudLlmProvider.Anthropic
                      ? "claude-3-5-sonnet-20241022"
                      : provider === CloudLlmProvider.GoogleGemini
                      ? "gemini-1.5-flash"
                      : "gpt-4o"
                  }
                />
              </label>

              <label className="field-group" htmlFor="cloud-embedding-model" style={{ flex: 1 }}>
                <SettingsFieldLabel text="Modello Embedding" tooltip="ID del modello di Embedding." />
                <input
                  id="cloud-embedding-model"
                  type="text"
                  value={embeddingModel}
                  onChange={(e) => setEmbeddingModel(e.target.value)}
                  placeholder={
                    provider === CloudLlmProvider.GoogleGemini
                      ? "text-embedding-004"
                      : "text-embedding-3-small"
                  }
                />
              </label>
            </div>

            {provider === CloudLlmProvider.AzureOpenAi && (
              <label className="field-group" htmlFor="azure-api-version">
                <SettingsFieldLabel text="Versione API Azure OpenAI" tooltip="es. 2024-02-15-preview" />
                <input
                  id="azure-api-version"
                  type="text"
                  value={apiVersion}
                  onChange={(e) => setApiVersion(e.target.value)}
                  placeholder="2024-02-15-preview"
                />
              </label>
            )}

            <label className="field-group" htmlFor="cloud-api-key">
              <SettingsFieldLabel
                text="API Key Segreta (Salvata in Windows Credential Manager)"
                tooltip="La tua API Key verrà protetta con cifratura hardware in Windows Credential Manager e non memorizzata nel DB."
              />
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <input
                  id="cloud-api-key"
                  type={showApiKey ? "text" : "password"}
                  value={apiKey}
                  onChange={(e) => setApiKey(e.target.value)}
                  placeholder={hasApiKey ? "•••••••••••••••• (API Key Già Salvata)" : "Inserisci API Key..."}
                  style={{ flex: 1 }}
                />
                <button
                  type="button"
                  className="button-secondary"
                  onClick={() => setShowApiKey(!showApiKey)}
                  style={{ padding: "0 0.75rem" }}
                >
                  {showApiKey ? "Nascondi" : "Mostra"}
                </button>
              </div>
            </label>
          </>
        )}

        <div className="settings-actions" style={{ marginTop: "1rem" }}>
          <button type="button" onClick={handleSave} disabled={saving || testing}>
            {saving ? "Salvataggio..." : "Salva Impostazioni Cloud"}
          </button>
          <button type="button" className="button-secondary" onClick={handleTest} disabled={testing || saving}>
            {testing ? "Verifica in corso..." : "Test Connessione Provider"}
          </button>
        </div>

        {statusMessage && (
          <div className="panel-note" style={{ marginTop: "0.5rem" }}>
            <p>{statusMessage}</p>
          </div>
        )}

        {testResult && (
          <div className={`panel-note ${testResult.success ? "" : "panel-note--warning"}`} style={{ marginTop: "0.5rem" }}>
            <p>
              <strong>{testResult.success ? "✓ Test Riuscito" : "✕ Test Fallito"}</strong> ({testResult.latencyMs}ms): {testResult.message}
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
