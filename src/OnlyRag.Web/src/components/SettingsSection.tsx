import { useEffect, useMemo, useState } from "react";
import {
  apiRequest,
  type DiagnosticsResponse,
  type OfficeConversionSettings,
  type OfficeConverterStatusResponse,
  type OcrSettings,
  type OllamaModel,
  type OllamaModelDetails,
  type OllamaSettings,
  type OllamaStatusResponse,
  type OperationMessageResponse,
  type PerformanceSettings
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";

type SettingsSectionProps = {
  settings: OllamaSettings | null;
  status: OllamaStatusResponse | null;
  models: OllamaModel[];
  loadError: string | null;
  onDataChanged: () => Promise<void>;
};

const emptySettings: OllamaSettings = {
  ollamaBaseUrl: "http://localhost:11434",
  defaultChatModel: null,
  defaultEmbeddingModel: null,
  defaultTranslationModel: null,
  requestTimeoutSeconds: 120,
  embeddingBatchSize: 1,
  embeddingNumCtx: null
};

const NUM_CTX_PRESETS = [512, 1024, 2048, 4096, 8192, 16384, 32768];

const emptyOfficeSettings: OfficeConversionSettings = {
  libreOfficePath: null,
  conversionTimeoutSeconds: 120
};

const emptyPerformanceSettings: PerformanceSettings = {
  maxParallelJobs: 1,
  maxOcrParallelPages: 1,
  embeddingBatchSize: 1,
  translationBatchSize: 1,
  maxContextChunks: 8,
  requestTimeoutSeconds: 120,
  enableLowResourceMode: false
};

const emptyOcrSettings: OcrSettings = {
  profile: "balanced",
  pdfDpi: 200,
  modelPreset: "PP-OCRv5",
  modelVersion: "PP-OCRv5",
  detectionSideLimit: 960,
  detectionThreshold: 0.3,
  detectionBoxThreshold: 0.6,
  detectionUnclipRatio: 1.5,
  recognitionScoreThreshold: 0.5,
  useTextlineOrientation: true,
  useDocumentOrientationClassification: false,
  useDocumentUnwarping: false,
  recognitionBatchSize: 6,
  cpuThreads: 2,
  device: "cpu"
};

export function SettingsSection({
  settings,
  status,
  models,
  loadError,
  onDataChanged
}: SettingsSectionProps) {
  const [formState, setFormState] = useState<OllamaSettings>(emptySettings);
  const [savedFormState, setSavedFormState] = useState<OllamaSettings>(emptySettings);
  const [officeFormState, setOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
  const [savedOfficeFormState, setSavedOfficeFormState] = useState<OfficeConversionSettings>(emptyOfficeSettings);
  const [performanceFormState, setPerformanceFormState] = useState<PerformanceSettings>(emptyPerformanceSettings);
  const [savedPerformanceFormState, setSavedPerformanceFormState] = useState<PerformanceSettings>(emptyPerformanceSettings);
  const [ocrFormState, setOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [savedOcrFormState, setSavedOcrFormState] = useState<OcrSettings>(emptyOcrSettings);
  const [officeStatus, setOfficeStatus] = useState<OfficeConverterStatusResponse | null>(null);
  const [diagnostics, setDiagnostics] = useState<DiagnosticsResponse | null>(null);
  const [modelToInstall, setModelToInstall] = useState("");
  const [infoMessage, setInfoMessage] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isBusy, setIsBusy] = useState(false);
  const [embeddingModelDetails, setEmbeddingModelDetails] = useState<OllamaModelDetails | null>(null);
  const [embeddingModelDetailsLoading, setEmbeddingModelDetailsLoading] = useState(false);

  useEffect(() => {
    if (settings) {
      const normalizedSettings = normalizeOllamaSettings(settings);
      setFormState(normalizedSettings);
      setSavedFormState(normalizedSettings);
      setPerformanceFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSettings.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSettings.embeddingBatchSize
      }));
      setSavedPerformanceFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSettings.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSettings.embeddingBatchSize
      }));
    }
  }, [settings]);

  useEffect(() => {
    void refreshOfficeConverter();
    void refreshPerformanceSettings();
    void refreshOcrSettings();
    void refreshDiagnostics();
  }, []);

  useEffect(() => {
    const modelName = formState.defaultEmbeddingModel;
    if (!modelName) {
      setEmbeddingModelDetails(null);
      return;
    }

    let cancelled = false;
    setEmbeddingModelDetailsLoading(true);
    apiRequest<OllamaModelDetails>(`/api/ollama/models/${encodeURIComponent(modelName)}/details`)
      .then((details) => { if (!cancelled) { setEmbeddingModelDetails(details); } })
      .catch(() => { if (!cancelled) { setEmbeddingModelDetails(null); } })
      .finally(() => { if (!cancelled) { setEmbeddingModelDetailsLoading(false); } });

    return () => { cancelled = true; };
  }, [formState.defaultEmbeddingModel]);

  const installedModelNames = useMemo(() => models.map((model) => model.name), [models]);
  const unavailableDefaults = useMemo(
    () =>
      [
        formState.defaultChatModel,
        formState.defaultEmbeddingModel,
        formState.defaultTranslationModel
      ].filter((value): value is string => Boolean(value && !installedModelNames.includes(value))),
    [
      formState.defaultChatModel,
      formState.defaultEmbeddingModel,
      formState.defaultTranslationModel,
      installedModelNames
    ]
  );
  const hasDirtyOllamaSettings = !areOllamaSettingsEqual(formState, savedFormState);
  const hasDirtyOfficeSettings = !areOfficeSettingsEqual(officeFormState, savedOfficeFormState);
  const hasDirtyPerformanceSettings = !arePerformanceSettingsEqual(
    performanceFormState,
    savedPerformanceFormState
  );
  const hasDirtyOcrSettings = !areOcrSettingsEqual(ocrFormState, savedOcrFormState);
  const hasPendingChanges =
    hasDirtyOllamaSettings || hasDirtyOfficeSettings || hasDirtyPerformanceSettings || hasDirtyOcrSettings;

  async function saveSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OllamaSettings>("/api/settings/ollama", {
        method: "PUT",
        body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
      });

      const normalizedSaved = normalizeOllamaSettings(saved);
      setFormState(normalizedSaved);
      setSavedFormState(normalizedSaved);
      setInfoMessage("Impostazioni Ollama salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le impostazioni.");
    } finally {
      setIsBusy(false);
    }
  }

  async function testConnection() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OllamaSettings>("/api/settings/ollama", {
        method: "PUT",
        body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
      });
      const normalizedSaved = normalizeOllamaSettings(saved);
      setFormState(normalizedSaved);
      setSavedFormState(normalizedSaved);

      const response = await apiRequest<OllamaStatusResponse>("/api/ollama/status");
      setInfoMessage(response.message);
      if (!response.isReachable && response.suggestion) {
        setErrorMessage(response.suggestion);
      }

      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Test connessione non riuscito.");
    } finally {
      setIsBusy(false);
    }
  }

  async function installModel() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>("/api/ollama/models/pull", {
        method: "POST",
        body: JSON.stringify({ name: modelToInstall })
      });

      setInfoMessage(response.message);
      setModelToInstall("");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Installazione modello non riuscita.");
    } finally {
      setIsBusy(false);
    }
  }

  async function removeModel(name: string) {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>(
        `/api/ollama/models/${encodeURIComponent(name)}`,
        {
          method: "DELETE"
        }
      );

      setInfoMessage(response.message);
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Rimozione modello non riuscita.");
    } finally {
      setIsBusy(false);
    }
  }

  async function refreshOfficeConverter() {
    try {
      const [officeSettings, converterStatus] = await Promise.all([
        apiRequest<OfficeConversionSettings>("/api/settings/office-conversion"),
        apiRequest<OfficeConverterStatusResponse>("/api/office-converter/status")
      ]);
      const normalizedOfficeSettings = normalizeOfficeSettings(officeSettings);
      setOfficeFormState(normalizedOfficeSettings);
      setSavedOfficeFormState(normalizedOfficeSettings);
      setOfficeStatus(converterStatus);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere il convertitore Office.");
    }
  }

  async function refreshPerformanceSettings() {
    try {
      const performance = await apiRequest<PerformanceSettings>("/api/settings/performance");
      const normalizedPerformance = normalizePerformanceSettings(performance);
      setPerformanceFormState(normalizedPerformance);
      setSavedPerformanceFormState(normalizedPerformance);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni prestazioni.");
    }
  }

  async function refreshOcrSettings() {
    try {
      const ocr = await apiRequest<OcrSettings>("/api/settings/ocr");
      const normalizedOcr = normalizeOcrSettings(ocr);
      setOcrFormState(normalizedOcr);
      setSavedOcrFormState(normalizedOcr);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile leggere le impostazioni OCR.");
    }
  }

  async function refreshDiagnostics() {
    try {
      const data = await apiRequest<DiagnosticsResponse>("/api/diagnostics");
      setDiagnostics(data);
    } catch {
      // Diagnostics are non-critical; silence the error to avoid overwriting other messages.
    }
  }

  async function openLogsFolder() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const response = await apiRequest<OperationMessageResponse>("/api/diagnostics/open-logs-folder", {
        method: "POST"
      });
      setInfoMessage(response.message);
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile aprire la cartella log.");
    } finally {
      setIsBusy(false);
    }
  }

  async function savePerformanceSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<PerformanceSettings>("/api/settings/performance", {
        method: "PUT",
        body: JSON.stringify(buildPerformanceSettingsPayload(performanceFormState))
      });

      const normalizedSaved = normalizePerformanceSettings(saved);
      setPerformanceFormState(normalizedSaved);
      setSavedPerformanceFormState(normalizedSaved);
      setFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSaved.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSaved.embeddingBatchSize
      }));
      setSavedFormState((current) => ({
        ...current,
        requestTimeoutSeconds: normalizedSaved.requestTimeoutSeconds,
        embeddingBatchSize: normalizedSaved.embeddingBatchSize
      }));
      setInfoMessage("Impostazioni prestazioni salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le prestazioni.");
    } finally {
      setIsBusy(false);
    }
  }

  async function saveOcrSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await apiRequest<OcrSettings>("/api/settings/ocr", {
        method: "PUT",
        body: JSON.stringify(buildOcrSettingsPayload(ocrFormState))
      });

      const normalizedSaved = normalizeOcrSettings(saved);
      setOcrFormState(normalizedSaved);
      setSavedOcrFormState(normalizedSaved);
      setInfoMessage("Impostazioni OCR salvate.");
      await onDataChanged();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare le impostazioni OCR.");
    } finally {
      setIsBusy(false);
    }
  }

  async function saveOfficeSettings() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await persistOfficeSettings();
      setOfficeFormState(saved);
      setSavedOfficeFormState(saved);
      setInfoMessage("Impostazioni convertitore Office salvate.");
      await refreshOfficeConverter();
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Impossibile salvare il convertitore Office.");
    } finally {
      setIsBusy(false);
    }
  }

  async function testOfficeConverter() {
    setIsBusy(true);
    setErrorMessage(null);
    setInfoMessage(null);

    try {
      const saved = await persistOfficeSettings();
      setOfficeFormState(saved);
      setSavedOfficeFormState(saved);
      const response = await apiRequest<OfficeConverterStatusResponse>("/api/office-converter/test", {
        method: "POST"
      });
      setOfficeStatus(response);
      setInfoMessage(response.message);
      if (!response.isAvailable && response.suggestion) {
        setErrorMessage(response.suggestion);
      }
    } catch (error) {
      setErrorMessage(error instanceof Error ? error.message : "Test convertitore Office non riuscito.");
    } finally {
      setIsBusy(false);
    }
  }

  async function persistOfficeSettings(): Promise<OfficeConversionSettings> {
    const saved = await apiRequest<OfficeConversionSettings>("/api/settings/office-conversion", {
      method: "PUT",
      body: JSON.stringify(buildOfficeSettingsPayload(officeFormState))
    });
    return normalizeOfficeSettings(saved);
  }

  async function persistAllDirtyChanges() {
    if (hasDirtyPerformanceSettings) {
      const savedPerformance = normalizePerformanceSettings(
        await apiRequest<PerformanceSettings>("/api/settings/performance", {
          method: "PUT",
          body: JSON.stringify(buildPerformanceSettingsPayload(performanceFormState))
        })
      );
      setPerformanceFormState(savedPerformance);
      setSavedPerformanceFormState(savedPerformance);
      setFormState((current) => ({
        ...current,
        requestTimeoutSeconds: savedPerformance.requestTimeoutSeconds,
        embeddingBatchSize: savedPerformance.embeddingBatchSize
      }));
      setSavedFormState((current) => ({
        ...current,
        requestTimeoutSeconds: savedPerformance.requestTimeoutSeconds,
        embeddingBatchSize: savedPerformance.embeddingBatchSize
      }));
    }

    if (hasDirtyOllamaSettings) {
      const savedSettings = normalizeOllamaSettings(
        await apiRequest<OllamaSettings>("/api/settings/ollama", {
          method: "PUT",
          body: JSON.stringify(buildOllamaSettingsPayload(formState, performanceFormState))
        })
      );
      setFormState(savedSettings);
      setSavedFormState(savedSettings);
    }

    if (hasDirtyOfficeSettings) {
      const savedOffice = await persistOfficeSettings();
      setOfficeFormState(savedOffice);
      setSavedOfficeFormState(savedOffice);
    }

    if (hasDirtyOcrSettings) {
      const savedOcr = normalizeOcrSettings(
        await apiRequest<OcrSettings>("/api/settings/ocr", {
          method: "PUT",
          body: JSON.stringify(buildOcrSettingsPayload(ocrFormState))
        })
      );
      setOcrFormState(savedOcr);
      setSavedOcrFormState(savedOcr);
    }
  }

  useEffect(() => {
    setExitContributor("settings", {
      label: "Impostazioni",
      hasPendingChanges,
      hasActiveWork: isBusy,
      prepareForExit: persistAllDirtyChanges
    });

    return () => {
      clearExitContributor("settings");
    };
  }, [
    formState,
    hasDirtyOfficeSettings,
    hasDirtyOllamaSettings,
    hasDirtyOcrSettings,
    hasDirtyPerformanceSettings,
    hasPendingChanges,
    isBusy,
    ocrFormState,
    officeFormState,
    performanceFormState
  ]);

  return (
    <div className="section-layout settings-layout">
      <div className="section-copy settings-copy">
        <h2>Impostazioni</h2>
      </div>
      <div className="settings-panel settings-panel--grid" aria-label="Impostazioni principali">
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
            <div className="settings-actions">
              <button type="button" onClick={saveSettings} disabled={isBusy}>
                Salva impostazioni
              </button>
              <button type="button" className="button-secondary" onClick={testConnection} disabled={isBusy}>
                Test connessione
              </button>
            </div>
            <div className="panel-note">
              <p>{status?.message ?? loadError ?? "Configura l'indirizzo Ollama e testa la connessione."}</p>
              {status?.suggestion && <p>{status.suggestion}</p>}
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Prestazioni</h3>
            {performanceFormState.enableLowResourceMode && (
              <span className="status-chip status-chip--offline">Modalità risparmio risorse</span>
            )}
          </div>
          <div className="settings-form">
            <label className="toggle-row" htmlFor="low-resource-mode">
              <input
                id="low-resource-mode"
                type="checkbox"
                checked={performanceFormState.enableLowResourceMode}
                onChange={(event) =>
                  setPerformanceFormState((current) => ({
                    ...current,
                    enableLowResourceMode: event.target.checked
                  }))
                }
              />
              <span>Modalità PC poco performante</span>
            </label>
            {performanceFormState.enableLowResourceMode && (
              <div className="panel-note" style={{ marginTop: 0 }}>
                <p>Forza job paralleli, batch OCR, embedding e traduzione a 1. Consigliato su macchine con meno di 8 GB di RAM o CPU lenta.</p>
              </div>
            )}
            <div className="settings-grid">
              <label className="field-group" htmlFor="max-parallel-jobs">
                <span>Job paralleli</span>
                <input
                  id="max-parallel-jobs"
                  type="number"
                  min={1}
                  max={4}
                  value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxParallelJobs}
                  disabled={performanceFormState.enableLowResourceMode}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      maxParallelJobs: Number(event.target.value)
                    }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-parallel-pages">
                <span>Pagine OCR parallele</span>
                <input
                  id="ocr-parallel-pages"
                  type="number"
                  min={1}
                  max={4}
                  value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.maxOcrParallelPages}
                  disabled={performanceFormState.enableLowResourceMode}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      maxOcrParallelPages: Number(event.target.value)
                    }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="performance-embedding-batch">
                <span>Batch embedding</span>
                <input
                  id="performance-embedding-batch"
                  type="number"
                  min={1}
                  max={8}
                  value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.embeddingBatchSize}
                  disabled={performanceFormState.enableLowResourceMode}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      embeddingBatchSize: Number(event.target.value)
                    }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="translation-batch-size">
                <span>Batch traduzione</span>
                <input
                  id="translation-batch-size"
                  type="number"
                  min={1}
                  max={4}
                  value={performanceFormState.enableLowResourceMode ? 1 : performanceFormState.translationBatchSize}
                  disabled={performanceFormState.enableLowResourceMode}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      translationBatchSize: Number(event.target.value)
                    }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="max-context-chunks">
                <span>Chunk contesto</span>
                <input
                  id="max-context-chunks"
                  type="number"
                  min={1}
                  max={24}
                  value={performanceFormState.maxContextChunks}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      maxContextChunks: Number(event.target.value)
                    }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="performance-request-timeout">
                <span>Timeout richieste</span>
                <input
                  id="performance-request-timeout"
                  type="number"
                  min={5}
                  max={600}
                  value={performanceFormState.requestTimeoutSeconds}
                  onChange={(event) =>
                    setPerformanceFormState((current) => ({
                      ...current,
                      requestTimeoutSeconds: Number(event.target.value)
                    }))
                  }
                />
              </label>
            </div>
            <div className="settings-actions">
              <button type="button" onClick={savePerformanceSettings} disabled={isBusy}>
                Salva prestazioni
              </button>
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>OCR PaddleOCR</h3>
            <span className="status-chip status-chip--muted">{ocrFormState.profile}</span>
          </div>
          <div className="settings-form">
            <div className="settings-grid">
              <label className="field-group" htmlFor="ocr-profile">
                <span>Profilo</span>
                <select
                  id="ocr-profile"
                  value={ocrFormState.profile}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, profile: event.target.value }))
                  }
                >
                  <option value="fast">Veloce</option>
                  <option value="balanced">Bilanciato</option>
                  <option value="accurate">Accurato</option>
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-device">
                <span>Dispositivo</span>
                <select
                  id="ocr-device"
                  value={ocrFormState.device}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, device: event.target.value }))
                  }
                >
                  <option value="cpu">CPU</option>
                  <option value="gpu">GPU</option>
                </select>
              </label>
              <label className="field-group" htmlFor="ocr-pdf-dpi">
                <span>DPI PDF</span>
                <input
                  id="ocr-pdf-dpi"
                  type="number"
                  min={96}
                  max={400}
                  value={ocrFormState.pdfDpi}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, pdfDpi: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-model-preset">
                <span>Preset modello</span>
                <input
                  id="ocr-model-preset"
                  type="text"
                  value={ocrFormState.modelPreset}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, modelPreset: event.target.value }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-model-version">
                <span>Versione modello</span>
                <input
                  id="ocr-model-version"
                  type="text"
                  value={ocrFormState.modelVersion}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, modelVersion: event.target.value }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-detection-side-limit">
                <span>Lato massimo detection</span>
                <input
                  id="ocr-detection-side-limit"
                  type="number"
                  min={320}
                  max={4096}
                  value={ocrFormState.detectionSideLimit}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, detectionSideLimit: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-detection-threshold">
                <span>Soglia detection</span>
                <input
                  id="ocr-detection-threshold"
                  type="number"
                  min={0.01}
                  max={0.99}
                  step={0.01}
                  value={ocrFormState.detectionThreshold}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, detectionThreshold: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-detection-box-threshold">
                <span>Soglia box</span>
                <input
                  id="ocr-detection-box-threshold"
                  type="number"
                  min={0.01}
                  max={0.99}
                  step={0.01}
                  value={ocrFormState.detectionBoxThreshold}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, detectionBoxThreshold: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-detection-unclip-ratio">
                <span>Unclip ratio</span>
                <input
                  id="ocr-detection-unclip-ratio"
                  type="number"
                  min={1}
                  max={3}
                  step={0.05}
                  value={ocrFormState.detectionUnclipRatio}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, detectionUnclipRatio: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-recognition-score-threshold">
                <span>Soglia riconoscimento</span>
                <input
                  id="ocr-recognition-score-threshold"
                  type="number"
                  min={0.01}
                  max={0.99}
                  step={0.01}
                  value={ocrFormState.recognitionScoreThreshold}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, recognitionScoreThreshold: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-recognition-batch-size">
                <span>Batch riconoscimento</span>
                <input
                  id="ocr-recognition-batch-size"
                  type="number"
                  min={1}
                  max={32}
                  value={ocrFormState.recognitionBatchSize}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, recognitionBatchSize: Number(event.target.value) }))
                  }
                />
              </label>
              <label className="field-group" htmlFor="ocr-cpu-threads">
                <span>Thread CPU</span>
                <input
                  id="ocr-cpu-threads"
                  type="number"
                  min={1}
                  max={16}
                  value={ocrFormState.cpuThreads}
                  onChange={(event) =>
                    setOcrFormState((current) => ({ ...current, cpuThreads: Number(event.target.value) }))
                  }
                />
              </label>
            </div>
            <label className="toggle-row" htmlFor="ocr-textline-orientation">
              <input
                id="ocr-textline-orientation"
                type="checkbox"
                checked={ocrFormState.useTextlineOrientation}
                onChange={(event) =>
                  setOcrFormState((current) => ({
                    ...current,
                    useTextlineOrientation: event.target.checked
                  }))
                }
              />
              <span>Orientamento righe testo</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-orientation">
              <input
                id="ocr-document-orientation"
                type="checkbox"
                checked={ocrFormState.useDocumentOrientationClassification}
                onChange={(event) =>
                  setOcrFormState((current) => ({
                    ...current,
                    useDocumentOrientationClassification: event.target.checked
                  }))
                }
              />
              <span>Classificazione orientamento documento</span>
            </label>
            <label className="toggle-row" htmlFor="ocr-document-unwarping">
              <input
                id="ocr-document-unwarping"
                type="checkbox"
                checked={ocrFormState.useDocumentUnwarping}
                onChange={(event) =>
                  setOcrFormState((current) => ({
                    ...current,
                    useDocumentUnwarping: event.target.checked
                  }))
                }
              />
              <span>Correzione deformazione documento</span>
            </label>
            <div className="settings-actions">
              <button type="button" onClick={saveOcrSettings} disabled={isBusy}>
                Salva OCR
              </button>
              {hasDirtyOcrSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
          </div>
        </div>

        <div className="settings-card settings-card--wide">
          <div className="settings-card__header">
            <h3>Modelli predefiniti</h3>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="default-chat-model">
              <span>Chat</span>
              <select
                id="default-chat-model"
                value={formState.defaultChatModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultChatModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="field-group" htmlFor="default-embedding-model">
              <span>Embeddings</span>
              <select
                id="default-embedding-model"
                value={formState.defaultEmbeddingModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultEmbeddingModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>

            {formState.defaultEmbeddingModel && (
              <div className="model-context-bar">
                <div className="model-context-bar__label">
                  <span>Finestra di contesto embedding (num_ctx)</span>
                  {embeddingModelDetailsLoading && <span className="model-context-bar__hint">Caricamento...</span>}
                  {!embeddingModelDetailsLoading && embeddingModelDetails?.numCtx && (
                    <span className="model-context-bar__hint">
                      Finestra nativa: {embeddingModelDetails.numCtx.toLocaleString()} token
                    </span>
                  )}
                </div>
                <div className="model-context-selector">
                  <select
                    id="embedding-num-ctx"
                    value={formState.embeddingNumCtx ?? "auto"}
                    onChange={(event) => {
                      const val = event.target.value;
                      setFormState((current) => ({
                        ...current,
                        embeddingNumCtx: val === "auto" ? null : Number(val)
                      }));
                    }}
                  >
                    <option value="auto">Automatico (adatta al chunk più lungo)</option>
                    {NUM_CTX_PRESETS.map((p) => (
                      <option key={p} value={p}>{p.toLocaleString()} token</option>
                    ))}
                    {formState.embeddingNumCtx != null
                      && !NUM_CTX_PRESETS.includes(formState.embeddingNumCtx) && (
                      <option value={formState.embeddingNumCtx}>
                        {formState.embeddingNumCtx.toLocaleString()} token (personalizzato)
                      </option>
                    )}
                  </select>
                  <input
                    type="number"
                    className="model-context-custom-input"
                    min={64}
                    max={131072}
                    placeholder="Valore personalizzato"
                    value={formState.embeddingNumCtx ?? ""}
                    onChange={(event) => {
                      const val = event.target.value;
                      setFormState((current) => ({
                        ...current,
                        embeddingNumCtx: val === "" ? null : Number(val)
                      }));
                    }}
                  />
                </div>
                {embeddingModelDetails?.numCtx && formState.embeddingNumCtx == null && (
                  <div className="model-context-bar__track">
                    <div
                      className="model-context-bar__fill"
                      style={{ width: "100%" }}
                      title={`Finestra nativa: ${embeddingModelDetails.numCtx.toLocaleString()} token`}
                    />
                    <span className="model-context-bar__track-label">
                      {embeddingModelDetails.numCtx.toLocaleString()} token (nativo)
                    </span>
                  </div>
                )}
                {formState.embeddingNumCtx != null && embeddingModelDetails?.numCtx && (
                  <div className="model-context-bar__track">
                    <div
                      className="model-context-bar__fill"
                      style={{
                        width: `${Math.min(100, Math.round((formState.embeddingNumCtx / embeddingModelDetails.numCtx) * 100))}%`
                      }}
                      title={`${formState.embeddingNumCtx.toLocaleString()} / ${embeddingModelDetails.numCtx.toLocaleString()} token`}
                    />
                    <span className="model-context-bar__track-label">
                      {formState.embeddingNumCtx.toLocaleString()} / {embeddingModelDetails.numCtx.toLocaleString()} token
                    </span>
                  </div>
                )}
              </div>
            )}
            <label className="field-group" htmlFor="default-translation-model">
              <span>Traduzione</span>
              <select
                id="default-translation-model"
                value={formState.defaultTranslationModel ?? ""}
                onChange={(event) =>
                  setFormState((current) => ({
                    ...current,
                    defaultTranslationModel: normalizeOptionalValue(event.target.value)
                  }))
                }
              >
                <option value="">Nessun modello selezionato</option>
                {models.map((model) => (
                  <option key={model.name} value={model.name}>
                    {model.name}
                  </option>
                ))}
              </select>
            </label>
            {unavailableDefaults.length > 0 && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>Alcuni modelli salvati non sono piu presenti in Ollama: {unavailableDefaults.join(", ")}.</p>
              </div>
            )}
            {hasDirtyOllamaSettings && (
              <div className="settings-actions settings-actions--dirty" aria-live="polite">
                <button type="button" onClick={saveSettings} disabled={isBusy}>
                  Salva modelli predefiniti
                </button>
                <span className="dirty-hint">Modifiche non salvate</span>
              </div>
            )}
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Supporto file Office vecchi (.doc, .xls, .ppt)</h3>
            <span className={`status-chip status-chip--${officeStatus?.isAvailable ? "online" : "offline"}`}>
              {officeStatus?.isAvailable ? "Disponibile" : "Non installato"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="libreoffice-path">
              <span>Percorso LibreOffice (opzionale — rilevato automaticamente se installato)</span>
              <input
                id="libreoffice-path"
                type="text"
                value={officeFormState.libreOfficePath ?? ""}
                onChange={(event) =>
                  setOfficeFormState((current) => ({
                    ...current,
                    libreOfficePath: normalizeOptionalValue(event.target.value)
                  }))
                }
                placeholder="C:\Program Files\LibreOffice\program\soffice.exe"
              />
            </label>
            <label className="field-group" htmlFor="office-conversion-timeout">
              <span>Timeout conversione (secondi)</span>
              <input
                id="office-conversion-timeout"
                type="number"
                min={10}
                max={900}
                value={officeFormState.conversionTimeoutSeconds}
                onChange={(event) =>
                  setOfficeFormState((current) => ({
                    ...current,
                    conversionTimeoutSeconds: Number(event.target.value)
                  }))
                }
              />
            </label>
            <div className="settings-actions">
              <button type="button" onClick={saveOfficeSettings} disabled={isBusy}>
                Salva
              </button>
            </div>
            {officeStatus?.executablePath && (
              <div className="panel-note">
                <p>Rilevato: {officeStatus.executablePath}</p>
              </div>
            )}
            {officeStatus?.suggestion && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>{officeStatus.suggestion}</p>
              </div>
            )}
          </div>
        </div>

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
            </div>
            <div className="model-list" aria-label="Modelli installati">
              {models.length === 0 && (
                <div className="model-row model-row--empty">
                  <p>Nessun modello installato.</p>
                </div>
              )}
              {models.map((model) => (
                <div className="model-row" key={model.name}>
                  <div>
                    <strong>{model.name}</strong>
                    <span>
                      {model.family ?? "Famiglia non indicata"} | {formatModelSize(model.size)}
                    </span>
                  </div>
                  <button
                    type="button"
                    className="button-danger"
                    onClick={() => void removeModel(model.name)}
                    disabled={isBusy}
                  >
                    Rimuovi
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Diagnostica</h3>
            {diagnostics && (
              <span className="status-chip status-chip--muted">v{diagnostics.appVersion}</span>
            )}
          </div>
          <div className="settings-form">
            {diagnostics ? (
              <>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Database</span>
                  <code className="diagnostic-value">{diagnostics.databasePath}</code>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Cartella log</span>
                  <code className="diagnostic-value">{diagnostics.logsDirectory}</code>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">Ollama</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ollamaIsReachable ? "online" : "offline"}`}
                  >
                    {diagnostics.ollamaStatus}
                  </span>
                </div>
                <div className="diagnostic-row">
                  <span className="diagnostic-label">OCR ({diagnostics.ocrEngineName})</span>
                  <span
                    className={`status-chip status-chip--${diagnostics.ocrIsConfigured ? "online" : "offline"}`}
                  >
                    {diagnostics.ocrStatus}
                  </span>
                </div>
                {!diagnostics.ocrIsConfigured && (
                  <div className="panel-note panel-note--warning" role="alert">
                    <p>Esegui <code>scripts\Bootstrap-Prerequisites.ps1</code> per abilitare OCR.</p>
                  </div>
                )}
              </>
            ) : (
              <div className="panel-note">
                <p>Dati diagnostici non disponibili.</p>
              </div>
            )}
            <div className="settings-actions">
              <button
                type="button"
                className="button-secondary"
                onClick={() => void refreshDiagnostics()}
                disabled={isBusy}
              >
                Aggiorna
              </button>
              <button type="button" onClick={() => void openLogsFolder()} disabled={isBusy}>
                Apri cartella log
              </button>
            </div>
          </div>
        </div>

        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}
      </div>
    </div>
  );
}

function normalizeOptionalValue(value: string | null): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0 ? null : normalized;
}

function normalizeOllamaSettings(settings: OllamaSettings): OllamaSettings {
  return {
    ollamaBaseUrl: settings.ollamaBaseUrl.trim(),
    defaultChatModel: normalizeOptionalValue(settings.defaultChatModel),
    defaultEmbeddingModel: normalizeOptionalValue(settings.defaultEmbeddingModel),
    defaultTranslationModel: normalizeOptionalValue(settings.defaultTranslationModel),
    requestTimeoutSeconds: Number(settings.requestTimeoutSeconds),
    embeddingBatchSize: Number(settings.embeddingBatchSize),
    embeddingNumCtx: settings.embeddingNumCtx != null ? Number(settings.embeddingNumCtx) : null
  };
}

function normalizeOfficeSettings(settings: OfficeConversionSettings): OfficeConversionSettings {
  return {
    libreOfficePath: normalizeOptionalValue(settings.libreOfficePath),
    conversionTimeoutSeconds: Number(settings.conversionTimeoutSeconds)
  };
}

function normalizePerformanceSettings(settings: PerformanceSettings): PerformanceSettings {
  return {
    maxParallelJobs: Number(settings.maxParallelJobs),
    maxOcrParallelPages: Number(settings.maxOcrParallelPages),
    embeddingBatchSize: Number(settings.embeddingBatchSize),
    translationBatchSize: Number(settings.translationBatchSize),
    maxContextChunks: Number(settings.maxContextChunks),
    requestTimeoutSeconds: Number(settings.requestTimeoutSeconds),
    enableLowResourceMode: settings.enableLowResourceMode
  };
}

function normalizeOcrSettings(settings: OcrSettings): OcrSettings {
  return {
    profile: settings.profile.trim(),
    pdfDpi: Number(settings.pdfDpi),
    modelPreset: settings.modelPreset.trim(),
    modelVersion: settings.modelVersion.trim(),
    detectionSideLimit: Number(settings.detectionSideLimit),
    detectionThreshold: Number(settings.detectionThreshold),
    detectionBoxThreshold: Number(settings.detectionBoxThreshold),
    detectionUnclipRatio: Number(settings.detectionUnclipRatio),
    recognitionScoreThreshold: Number(settings.recognitionScoreThreshold),
    useTextlineOrientation: settings.useTextlineOrientation,
    useDocumentOrientationClassification: settings.useDocumentOrientationClassification,
    useDocumentUnwarping: settings.useDocumentUnwarping,
    recognitionBatchSize: Number(settings.recognitionBatchSize),
    cpuThreads: Number(settings.cpuThreads),
    device: settings.device.trim()
  };
}

function buildOllamaSettingsPayload(
  formState: OllamaSettings,
  performanceFormState: PerformanceSettings
): OllamaSettings {
  return normalizeOllamaSettings({
    ...formState,
    requestTimeoutSeconds: Number(performanceFormState.requestTimeoutSeconds),
    embeddingBatchSize: Number(performanceFormState.embeddingBatchSize)
  });
}

function buildOfficeSettingsPayload(
  officeFormState: OfficeConversionSettings
): OfficeConversionSettings {
  return normalizeOfficeSettings(officeFormState);
}

function buildPerformanceSettingsPayload(
  performanceFormState: PerformanceSettings
): PerformanceSettings {
  return normalizePerformanceSettings(performanceFormState);
}

function buildOcrSettingsPayload(ocrFormState: OcrSettings): OcrSettings {
  return normalizeOcrSettings(ocrFormState);
}

function areOllamaSettingsEqual(left: OllamaSettings, right: OllamaSettings): boolean {
  return JSON.stringify(normalizeOllamaSettings(left)) === JSON.stringify(normalizeOllamaSettings(right));
}

function areOfficeSettingsEqual(
  left: OfficeConversionSettings,
  right: OfficeConversionSettings
): boolean {
  return JSON.stringify(normalizeOfficeSettings(left)) === JSON.stringify(normalizeOfficeSettings(right));
}

function arePerformanceSettingsEqual(left: PerformanceSettings, right: PerformanceSettings): boolean {
  return JSON.stringify(normalizePerformanceSettings(left)) === JSON.stringify(normalizePerformanceSettings(right));
}

function areOcrSettingsEqual(left: OcrSettings, right: OcrSettings): boolean {
  return JSON.stringify(normalizeOcrSettings(left)) === JSON.stringify(normalizeOcrSettings(right));
}

function formatModelSize(size: number): string {
  if (size >= 1_000_000_000) {
    return `${(size / 1_000_000_000).toFixed(1)} GB`;
  }

  if (size >= 1_000_000) {
    return `${(size / 1_000_000).toFixed(1)} MB`;
  }

  return `${size} B`;
}
