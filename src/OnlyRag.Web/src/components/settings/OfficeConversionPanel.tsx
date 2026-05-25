import {
  SettingsRangeField,
  normalizeOptionalValue
} from "../SettingsSection.helpers";
import { useSettingsSectionContext } from "../SettingsSectionContext";

export function OfficeConversionPanel() {
  const {
    officeStatus,
    officeFormState,
    setOfficeFormState,
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
              <span>Percorso LibreOffice</span>
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
            <SettingsRangeField
              id="office-conversion-timeout"
              label="Timeout conversione"
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
              <button type="button" onClick={saveOfficeSettings} disabled={isBusy}>
                Salva
              </button>
              {officeStatus && !officeStatus.isAvailable && (
                <button type="button" className="button-secondary" onClick={openLibreOfficeDownload} disabled={isBusy}>
                  Scarica LibreOffice
                </button>
              )}
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

