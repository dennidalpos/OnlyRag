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

export function SettingsSectionPanels() {
  const { errorMessage, infoMessage } = useSettingsSectionContext();
  const [activeTab, setActiveTab] = useState<SettingsTab>("all");

  const isVisible = (tab: SettingsTab) => activeTab === "all" || activeTab === tab;

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
            <span>{tab.label}</span>
          </button>
        ))}
      </nav>

      <div className="settings-panel" aria-label="Impostazioni principali">
        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}

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


