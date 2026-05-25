import {
  SettingsFieldLabel,
  SettingsRangeField,
  normalizeOptionalValue
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OfficeConversionPanel() {
  const {
    officeStatus,
    officeFormState,
    setOfficeFormState,
    hasDirtyOfficeSettings,
    saveOfficeSettings,
    isBusy,
    openLibreOfficeDownload
  } = useSettingsSectionContext();

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Office legacy (.doc, .xls, .ppt)</h3>
            <span className={`status-chip status-chip--${officeStatus?.isAvailable ? "online" : "offline"}`}>
              {officeStatus?.isAvailable ? "Disponibile" : "Non installato"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="libreoffice-path">
              <SettingsFieldLabel
                text="Percorso LibreOffice"
                tooltip="Percorso opzionale di soffice.exe per convertire file Office legacy."
              />
              <input
                id="libreoffice-path"
                type="text"
                value={officeFormState.libreOfficePath ?? ""}
                title="Percorso opzionale di soffice.exe per convertire file Office legacy."
                aria-label="Percorso LibreOffice"
                onChange={(event) =>
                  setOfficeFormState((current) => ({
                    ...current,
                    libreOfficePath: normalizeOptionalValue(event.target.value)
                  }))
                }
                placeholder="C:\Program Files\LibreOffice\program\soffice.exe"
              />
            </label>
            <SettingsRangeField
              id="office-conversion-timeout"
              label="Timeout conversione"
              tooltip="Tempo massimo concesso a LibreOffice per convertire un documento legacy."
              min={10}
              max={900}
              step={10}
              value={officeFormState.conversionTimeoutSeconds}
              formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
              onChange={(value) =>
                setOfficeFormState((current) => ({ ...current, conversionTimeoutSeconds: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={saveOfficeSettings} disabled={isBusy || !hasDirtyOfficeSettings}>
                Salva
              </button>
              {officeStatus && !officeStatus.isAvailable && (
                <button type="button" className="button-secondary" onClick={openLibreOfficeDownload} disabled={isBusy}>
                  Scarica LibreOffice
                </button>
              )}
              {hasDirtyOfficeSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
            {officeStatus?.executablePath && (
              <div className="panel-note panel-note--path">
                <p title={officeStatus.executablePath} aria-label={`Percorso rilevato: ${officeStatus.executablePath}`}>
                  {officeStatus.executablePath}
                </p>
              </div>
            )}
            {officeStatus?.suggestion && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>{officeStatus.suggestion}</p>
              </div>
            )}
          </div>
        </div>
  );
}

