import { DefaultModelsPanel } from "./settings/DefaultModelsPanel";
import { DiagnosticsPanel } from "./settings/DiagnosticsPanel";
import { IngestionSettingsPanel } from "./settings/IngestionSettingsPanel";
import { ModelManagementPanel } from "./settings/ModelManagementPanel";
import { OcrRuntimeSettingsPanel } from "./settings/OcrRuntimeSettingsPanel";
import { OfficeConversionPanel } from "./settings/OfficeConversionPanel";
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
          <h3 id="settings-connections-title">Connessioni</h3>
          <div className="settings-section__grid">
            <OllamaConnectionPanel />
            <DefaultModelsPanel />
            <ModelManagementPanel />
          </div>
        </section>
        <section className="settings-section" aria-labelledby="settings-performance-title">
          <h3 id="settings-performance-title">Prestazioni</h3>
          <div className="settings-section__grid">
            <PerformanceSettingsPanel />
          </div>
        </section>
        <section className="settings-section" aria-labelledby="settings-ocr-title">
          <h3 id="settings-ocr-title">OCR</h3>
          <div className="settings-section__grid">
            <OcrEngineSettingsPanel />
            <OcrRuntimeSettingsPanel />
          </div>
        </section>
        <section className="settings-section" aria-labelledby="settings-ingestion-title">
          <h3 id="settings-ingestion-title">Ingestione</h3>
          <div className="settings-section__grid">
            <IngestionSettingsPanel />
            <OfficeConversionPanel />
          </div>
        </section>
        <section className="settings-section" aria-labelledby="settings-diagnostics-title">
          <h3 id="settings-diagnostics-title">Diagnostica</h3>
          <div className="settings-section__grid">
            <DiagnosticsPanel />
          </div>
        </section>
      </div>
    </div>
  );
}


