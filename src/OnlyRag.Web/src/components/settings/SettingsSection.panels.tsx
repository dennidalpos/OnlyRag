import { useState } from "react";
import { DefaultModelsPanel } from "./DefaultModelsPanel";
import { DiagnosticsPanel } from "./DiagnosticsPanel";
import { IngestionSettingsPanel } from "./IngestionSettingsPanel";
import { LoggingSettingsPanel } from "./LoggingSettingsPanel";
import { ModelManagementPanel } from "./ModelManagementPanel";
import { OllamaConnectionPanel } from "./OllamaConnectionPanel";
import { CloudProviderPanel } from "./CloudProviderPanel";
import { OcrEngineSettingsPanel } from "./OcrEngineSettingsPanel";
import { PdfExportPanel } from "./PdfExportPanel";
import { PerformanceSettingsPanel } from "./PerformanceSettingsPanel";
import { RerankerModelPanel } from "./RerankerModelPanel";
import { ThemeSelectorPanel } from "./SettingsSection.fields";
import { HardwareMonitorPanel } from "./HardwareMonitorPanel";
import { AlertCard, AlertCardVariant } from "../common/AlertCard";
import { useSettingsSectionContext } from "./SettingsSectionContext";

type SettingsTab = "all" | "theme" | "models" | "rag" | "ocr" | "diagnostics";

const SETTINGS_TABS: { id: SettingsTab; label: string }[] = [
  { id: "all", label: "Tutte" },
  { id: "theme", label: "Tema" },
  { id: "models", label: "Modelli AI" },
  { id: "rag", label: "RAG & GPU" },
  { id: "ocr", label: "OCR & Export" },
  { id: "diagnostics", label: "Diagnostica" }
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
    variant: AlertCardVariant;
    title: string;
    detail?: string;
    actionLabel?: string;
    onAction?: () => void;
    isActionBusy?: boolean;
  }[] = [];

  // 1. Ollama offline (TASK-037 #3 & TASK-039)
  if (diagnostics && !diagnostics.ollamaIsReachable) {
    alerts.push({
      id: "ollama-offline",
      variant: "warning",
      title: "Connessione a Ollama non riuscita.",
      detail: "Verifica che Ollama sia in esecuzione per usare chat, embedding, traduzione e coding.",
      actionLabel: "Aggiorna stato",
      onAction: () => void refreshDiagnostics()
    });
  }

  // 2. Default models unavailable
  if (diagnostics && diagnostics.ollamaIsReachable && unavailableDefaults && unavailableDefaults.length > 0) {
    alerts.push({
      id: "models-unavailable",
      variant: "warning",
      title: "Modelli predefiniti mancanti in Ollama.",
      detail: `Modelli configurati non presenti in Ollama: ${unavailableDefaults.join(", ")}.`
    });
  }

  // 3. Reranker not installed (TASK-037 #4 & TASK-039)
  const isRerankerDownloaded = rerankerModelInfo?.isDownloaded ?? false;
  const isRerankerDownloading = rerankerModelInfo?.isDownloading ?? false;
  if (rerankerModelInfo && !isRerankerDownloaded && !isRerankerDownloading) {
    alerts.push({
      id: "reranker-missing",
      variant: "warning",
      title: "Modello di riclassificazione non installato. I risultati RAG saranno meno precisi.",
      detail: "Senza il Re-Ranker ONNX la ricerca usa un fallback euristico meno preciso.",
      actionLabel: "Scarica Re-Ranker",
      onAction: () => void downloadRerankerModel(),
      isActionBusy: isBusy
    });
  }

  // 4. OCR not configured (TASK-039)
  const isOcrConfigured = diagnostics?.ocrIsConfigured ?? false;
  const isOcrRunning = ocrProvisionStatus?.isRunning ?? false;
  if (diagnostics && !isOcrConfigured && !isOcrRunning) {
    alerts.push({
      id: "ocr-missing",
      variant: "warning",
      title: "Runtime OCR locale non configurato.",
      detail: "Il runtime OCR è richiesto per estrarre testo da scansioni, PDF e file Office.",
      actionLabel: "Installa OCR",
      onAction: () => void configureOcrRuntime("auto"),
      isActionBusy: isBusy
    });
  }

  // 5. LibreOffice PDF export not available (TASK-039)
  if (pdfExportStatus && !pdfExportStatus.isAvailable) {
    alerts.push({
      id: "libreoffice-missing",
      variant: "warning",
      title: "Export PDF non disponibile.",
      detail: pdfExportStatus.message ?? "LibreOffice è necessario per esportare documenti e traduzioni in PDF.",
      actionLabel: "Scarica LibreOffice",
      onAction: () => void openLibreOfficeDownload()
    });
  }

  // 6. Qdrant errors/warnings
  const isQdrantStarting =
    diagnostics?.qdrant.status === "Caricamento" ||
    diagnostics?.qdrant.status === "In caricamento..." ||
    diagnostics?.qdrant.status === "Starting";

  if (diagnostics && diagnostics.qdrant.error && !isQdrantStarting) {
    alerts.push({
      id: "qdrant-error",
      variant: "error",
      title: "Errore database vettoriale Qdrant.",
      detail: diagnostics.qdrant.error
    });
  } else if (diagnostics && diagnostics.qdrant.warning && !isQdrantStarting) {
    alerts.push({
      id: "qdrant-warning",
      variant: "warning",
      title: "Avviso database vettoriale Qdrant.",
      detail: diagnostics.qdrant.warning
    });
  }

  if (alerts.length === 0) return null;

  return (
    <div className="settings-alerts-container" style={{ display: "grid", gap: "10px", marginBottom: "20px" }}>
      {alerts.map((alert) => (
        <AlertCard
          key={alert.id}
          id={alert.id}
          variant={alert.variant}
          title={alert.title}
          detail={alert.detail}
          actionLabel={alert.actionLabel}
          onAction={alert.onAction}
          isActionBusy={alert.isActionBusy}
        />
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
      case "ocr": {
        const isOcrConfigured = diagnostics?.ocrIsConfigured ?? false;
        const isOcrRunning = ocrProvisionStatus?.isRunning ?? false;
        const isOcrMissing = diagnostics !== null && !isOcrConfigured && !isOcrRunning;
        const isPdfExportMissing = pdfExportStatus !== null && !pdfExportStatus.isAvailable;
        return isOcrMissing || isPdfExportMissing;
      }
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
              <CloudProviderPanel />
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


