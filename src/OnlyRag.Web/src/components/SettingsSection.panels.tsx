import { useState } from "react";
import { DefaultModelsPanel } from "./settings/DefaultModelsPanel";
import { DiagnosticsPanel } from "./settings/DiagnosticsPanel";
import { IngestionSettingsPanel } from "./settings/IngestionSettingsPanel";
import { LoggingSettingsPanel } from "./settings/LoggingSettingsPanel";
import { ModelManagementPanel } from "./settings/ModelManagementPanel";
import { OllamaConnectionPanel } from "./settings/OllamaConnectionPanel";
import { OcrEngineSettingsPanel } from "./settings/OcrEngineSettingsPanel";
import { PdfExportPanel } from "./settings/PdfExportPanel";
import { PerformanceSettingsPanel } from "./settings/PerformanceSettingsPanel";
import { RerankerModelPanel } from "./settings/RerankerModelPanel";
import { ThemeSelectorPanel } from "./SettingsSection.fields";
import { useSettingsSectionContext } from "./SettingsSectionContext";

type SettingsTab = "all" | "theme" | "models" | "rag" | "ocr" | "diagnostics";

const SETTINGS_TABS: { id: SettingsTab; label: string; icon: string }[] = [
  { id: "all", label: "Tutte", icon: "⚙️" },
  { id: "theme", label: "Tema & Generale", icon: "🎨" },
  { id: "models", label: "Connessione & Modelli", icon: "🤖" },
  { id: "rag", label: "RAG & Prestazioni", icon: "⚡" },
  { id: "ocr", label: "OCR & Documenti", icon: "📄" },
  { id: "diagnostics", label: "Manutenzione & Diagnostica", icon: "🛠️" }
];

export function SettingsSectionPanels() {
  const { errorMessage, infoMessage } = useSettingsSectionContext();
  const [activeTab, setActiveTab] = useState<SettingsTab>("all");

  const isVisible = (tab: SettingsTab) => activeTab === "all" || activeTab === tab;

  return (
    <div className="section-layout settings-layout">
      <div className="section-copy settings-copy">
        <h2>Impostazioni</h2>
      </div>

      <nav className="settings-nav-tabs" aria-label="Sezioni impostazioni">
        {SETTINGS_TABS.map((tab) => (
          <button
            key={tab.id}
            type="button"
            className={`settings-tab-btn ${activeTab === tab.id ? "settings-tab-btn--active" : ""}`}
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

