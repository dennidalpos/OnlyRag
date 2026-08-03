import { useState } from "react";
import { DefaultModelsPanel } from "./DefaultModelsPanel";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { IngestionSettingsPanel } from "./IngestionSettingsPanel";
import { LoggingSettingsPanel } from "./LoggingSettingsPanel";
import { ModelManagementPanel } from "./ModelManagementPanel";
import { OllamaConnectionPanel } from "./OllamaConnectionPanel";
import { OcrEngineSettingsPanel } from "./OcrEngineSettingsPanel";
import { PdfExportPanel } from "./PdfExportPanel";
import { PerformanceSettingsPanel } from "./PerformanceSettingsPanel";
import { RerankerModelPanel } from "./RerankerModelPanel";
import { ThemeSelectorPanel } from "./SettingsSection.fields";
import { HardwareMonitorPanel } from "./HardwareMonitorPanel";
import { RagBenchmarkPanel } from "./RagBenchmarkPanel";
import { useSettingsSectionContext } from "./SettingsSectionContext";

type SettingsTab = "all" | "theme" | "models" | "rag" | "ocr" | "diagnostics";

const SETTINGS_TABS: { id: SettingsTab; label: string; icon: string }[] = [
  { id: "all", label: "Tutte le opzioni", icon: "⚙️" },
  { id: "theme", label: "Tema & Grafica", icon: "🎨" },
  { id: "models", label: "Ollama & Modelli", icon: "🤖" },
  { id: "rag", label: "RAG & Prestazioni", icon: "⚡" },
  { id: "ocr", label: "OCR & PDF Export", icon: "📄" },
  { id: "diagnostics", label: "Log & Diagnostica", icon: "🛠️" }
];

export function SettingsConfigurationAlerts() {
  const {
    diagnostics,
    rerankerModelInfo,
    pdfExportStatus,
    unavailableDefaults,
    downloadRerankerModel,
    configureOcrRuntime,
    openLibreOfficeDownload,
    refreshDiagnostics,
    ocrProvisionStatus,
    isBusy
  } = useSettingsSectionContext();

  const alerts: {
    id: string;
    message: string;
    actionLabel?: string;
    onAction?: () => void;
    isActionBusy?: boolean;
  }[] = [];

  // 1. Ollama offline
  if (diagnostics && !diagnostics.ollamaIsReachable) {
    alerts.push({
      id: "ollama-offline",
      message: "Connessione a Ollama non riuscita. Assicurati che Ollama sia in esecuzione localmente per caricare i modelli di Chat, Embedding, Traduzione e Coding.",
      actionLabel: "Aggiorna stato",
      onAction: () => void refreshDiagnostics()
    });
  }

  // 2. Default models unavailable
  if (diagnostics && diagnostics.ollamaIsReachable && unavailableDefaults && unavailableDefaults.length > 0) {
    alerts.push({
      id: "models-unavailable",
      message: `Alcuni modelli salvati come predefiniti non sono presenti in Ollama: ${unavailableDefaults.join(", ")}.`,
    });
  }

  // 3. Reranker not installed
  const isRerankerDownloaded = rerankerModelInfo?.isDownloaded ?? false;
  const isRerankerDownloading = rerankerModelInfo?.isDownloading ?? false;
  if (rerankerModelInfo && !isRerankerDownloaded && !isRerankerDownloading) {
    alerts.push({
      id: "reranker-missing",
      message: "Il modello ONNX Cross-Encoder (Re-Ranker) non è installato. La ri-classificazione avanzata dei risultati RAG utilizzerà un fallback euristico a precisione ridotta.",
      actionLabel: "Scarica modello Re-Ranker",
      onAction: () => void downloadRerankerModel(),
      isActionBusy: isBusy
    });
  }

  // 4. OCR not configured
  const isOcrConfigured = diagnostics?.ocrIsConfigured ?? false;
  const isOcrRunning = ocrProvisionStatus?.isRunning ?? false;
  if (diagnostics && !isOcrConfigured && !isOcrRunning) {
    alerts.push({
      id: "ocr-missing",
      message: "Runtime OCR locale non configurato o incompleto. L'estrazione di testo da immagini, file Office e PDF digitalizzati non sarà disponibile.",
      actionLabel: "Installa OCR",
      onAction: () => void configureOcrRuntime("auto"),
      isActionBusy: isBusy
    });
  }

  // 5. LibreOffice PDF export not available
  if (pdfExportStatus && !pdfExportStatus.isAvailable) {
    alerts.push({
      id: "libreoffice-missing",
      message: "LibreOffice non è configurato. L'esportazione dei documenti e delle traduzioni in formato PDF non è disponibile.",
      actionLabel: "Scarica LibreOffice",
      onAction: () => void openLibreOfficeDownload()
    });
  }

  // 6. Qdrant errors/warnings
  if (diagnostics && diagnostics.qdrant.error) {
    alerts.push({
      id: "qdrant-error",
      message: `Errore database vettoriale Qdrant: ${diagnostics.qdrant.error}`
    });
  } else if (diagnostics && diagnostics.qdrant.warning) {
    alerts.push({
      id: "qdrant-warning",
      message: `Avviso database vettoriale Qdrant: ${diagnostics.qdrant.warning}`
    });
  }

  if (alerts.length === 0) return null;

  return (
    <div className="settings-alerts-container" style={{ display: "grid", gap: "12px", marginBottom: "20px" }}>
      {alerts.map((alert) => (
        <div
          key={alert.id}
          className="feedback-banner feedback-banner--warning settings-feedback-alert"
          role="alert"
          style={{ display: "flex", flexWrap: "wrap", justifyContent: "space-between", alignItems: "center", gap: "12px" }}
        >
          <div style={{ flex: "1 1 500px", display: "flex", alignItems: "center", gap: "10px" }}>
            <span style={{ fontSize: "16px" }}>⚠️</span>
            <p style={{ margin: 0, fontSize: "13px", lineHeight: "1.4" }}>{alert.message}</p>
          </div>
          {alert.onAction && (
            <button
              type="button"
              className="button-secondary"
              style={{ minHeight: "30px", height: "30px", padding: "0 10px", fontSize: "12px", background: "rgba(245, 158, 11, 0.15)", borderColor: "#f59e0b", color: "#fde047" }}
              onClick={alert.onAction}
              disabled={alert.isActionBusy}
            >
              {alert.actionLabel}
            </button>
          )}
        </div>
      ))}
    </div>
  );
}

export function SettingsSectionPanels() {
  const {
    errorMessage,
    infoMessage,
    diagnostics,
    rerankerModelInfo,
    pdfExportStatus,
    unavailableDefaults,
    ocrProvisionStatus
  } = useSettingsSectionContext();
  const [activeTab, setActiveTab] = useState<SettingsTab>("all");

  const isVisible = (tab: SettingsTab) => activeTab === "all" || activeTab === tab;

  const hasWarning = (tabId: SettingsTab) => {
    switch (tabId) {
      case "models":
        return Boolean(
          (diagnostics && !diagnostics.ollamaIsReachable) ||
          (diagnostics && diagnostics.ollamaIsReachable && unavailableDefaults && unavailableDefaults.length > 0)
        );
      case "rag":
        return Boolean(
          rerankerModelInfo && !rerankerModelInfo.isDownloaded && !rerankerModelInfo.isDownloading
        );
      case "ocr":
        const isOcrConfigured = diagnostics?.ocrIsConfigured ?? false;
        const isOcrRunning = ocrProvisionStatus?.isRunning ?? false;
        const isOcrMissing = diagnostics !== null && !isOcrConfigured && !isOcrRunning;
        const isPdfExportMissing = pdfExportStatus !== null && !pdfExportStatus.isAvailable;
        return isOcrMissing || isPdfExportMissing;
      case "diagnostics":
        return Boolean(
          diagnostics && (diagnostics.qdrant.warning || diagnostics.qdrant.error)
        );
      case "all":
        return (
          Boolean((diagnostics && !diagnostics.ollamaIsReachable) || (diagnostics && diagnostics.ollamaIsReachable && unavailableDefaults && unavailableDefaults.length > 0)) ||
          Boolean(rerankerModelInfo && !rerankerModelInfo.isDownloaded && !rerankerModelInfo.isDownloading) ||
          Boolean((diagnostics && !diagnostics.ocrIsConfigured && !ocrProvisionStatus?.isRunning) || (pdfExportStatus && !pdfExportStatus.isAvailable)) ||
          Boolean(diagnostics && (diagnostics.qdrant.warning || diagnostics.qdrant.error))
        );
      default:
        return false;
    }
  };

  return (
    <div className="settings-categorized-shell">
      <h2 className="sr-only">Impostazioni</h2>
      <nav className="settings-tab-nav" aria-label="Sezioni impostazioni">
        <h3 className="text-xs font-bold uppercase tracking-wider text-muted px-2 py-1">Categorie</h3>
        {SETTINGS_TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className={`settings-tab-button ${activeTab === tab.id ? "settings-tab-button--active" : ""}`}
            onClick={() => setActiveTab(tab.id)}
          >
            <span>{tab.icon}</span>
            <span style={{ flexGrow: 1 }}>{tab.label}</span>
            {hasWarning(tab.id) && (
              <span className="settings-tab-warning-dot" title="Configurazione incompleta o errore" />
            )}
          </button>
        ))}
      </nav>

      <div className="settings-panel" aria-label="Impostazioni principali">
        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}

        <SettingsConfigurationAlerts />

        {isVisible("theme") && (
          <section className="settings-section" aria-labelledby="settings-theme-title">
            <h3 id="settings-theme-title">Personalizzazione Visiva</h3>
            <div className="settings-card">
              <ThemeSelectorPanel />
            </div>
          </section>
        )}

        {isVisible("models") && (
          <section className="settings-section" aria-labelledby="settings-connections-title">
            <h3 id="settings-connections-title">Connessione &amp; Modelli</h3>
            <div className="settings-section__grid">
              <OllamaConnectionPanel />
              <DefaultModelsPanel />
              <ModelManagementPanel />
            </div>
          </section>
        )}

        {isVisible("rag") && (
          <section className="settings-section" aria-labelledby="settings-rag-title">
            <h3 id="settings-rag-title">Ricerca RAG &amp; Prestazioni</h3>
            <div className="settings-section__grid">
              <PerformanceSettingsPanel />
              <HardwareMonitorPanel />
              <IngestionSettingsPanel />
              <RerankerModelPanel />
            </div>
            <div className="mt-6">
              <RagBenchmarkPanel />
            </div>
          </section>
        )}

        {isVisible("ocr") && (
          <section className="settings-section" aria-labelledby="settings-ocr-title">
            <h3 id="settings-ocr-title">OCR &amp; Documenti</h3>
            <div className="settings-section__grid">
              <OcrEngineSettingsPanel />
              <PdfExportPanel />
            </div>
          </section>
        )}

        {isVisible("diagnostics") && (
          <section className="settings-section" aria-labelledby="settings-diagnostics-title">
            <h3 id="settings-diagnostics-title">Manutenzione &amp; Diagnostica</h3>
            <div className="settings-section__grid">
              <LoggingSettingsPanel />
              <DiagnosticsPanel />
            </div>
          </section>
        )}
      </div>
    </div>
  );
}



