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
      <div className="settings-panel settings-panel--grid" aria-label="Impostazioni principali">
        <OllamaConnectionPanel />
        <PerformanceSettingsPanel />
        <OcrEngineSettingsPanel />
        <IngestionSettingsPanel />
        <OcrRuntimeSettingsPanel />
        <DefaultModelsPanel />
        <OfficeConversionPanel />
        <ModelManagementPanel />
        <DiagnosticsPanel />

        {infoMessage && <div className="feedback-banner feedback-banner--info settings-feedback" role="status">{infoMessage}</div>}
        {errorMessage && <div className="feedback-banner feedback-banner--error settings-feedback" role="alert">{errorMessage}</div>}
      </div>
    </div>
  );
}


