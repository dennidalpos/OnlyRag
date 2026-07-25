import { DefaultModelsPanel } from "./settings/DefaultModelsPanel";
import { DiagnosticsPanel } from "./settings/DiagnosticsPanel";
import { IngestionSettingsPanel } from "./settings/IngestionSettingsPanel";
import { LoggingSettingsPanel } from "./settings/LoggingSettingsPanel";
import { ModelManagementPanel } from "./settings/ModelManagementPanel";
import { OllamaConnectionPanel } from "./settings/OllamaConnectionPanel";
import { OcrEngineSettingsPanel } from "./settings/OcrEngineSettingsPanel";
import { PerformanceSettingsPanel } from "./settings/PerformanceSettingsPanel";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function SettingsSectionPanels() {
  const { errorMessage, infoMessage } = useSettingsSectionContext();

  return (
    <div className="section-layout settings-layout">
      <div className="section-copy settings-copy">
        <h2>Impostazioni</h2>
      </div>
      <div className="settings-panel" aria-label="Impostazioni principali">
        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}

        <section className="settings-section" aria-labelledby="settings-connections-title">
          <h3 id="settings-connections-title">Connessione &amp; Modelli</h3>
          <div className="settings-section__grid">
            <OllamaConnectionPanel />
            <DefaultModelsPanel />
            <ModelManagementPanel />
          </div>
        </section>

        <section className="settings-section" aria-labelledby="settings-rag-title">
          <h3 id="settings-rag-title">Ricerca RAG &amp; Prestazioni</h3>
          <div className="settings-section__grid">
            <PerformanceSettingsPanel />
            <IngestionSettingsPanel />
          </div>
        </section>

        <section className="settings-section" aria-labelledby="settings-ocr-title">
          <h3 id="settings-ocr-title">OCR &amp; Documenti</h3>
          <div className="settings-section__grid">
            <OcrEngineSettingsPanel />
          </div>
        </section>

        <section className="settings-section" aria-labelledby="settings-diagnostics-title">
          <h3 id="settings-diagnostics-title">Manutenzione &amp; Diagnostica</h3>
          <div className="settings-section__grid">
            <LoggingSettingsPanel />
            <DiagnosticsPanel />
          </div>
        </section>
      </div>
    </div>
  );
}
