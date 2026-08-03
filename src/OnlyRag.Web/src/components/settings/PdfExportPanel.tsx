import {
  SettingsFieldLabel,
  SettingsRangeField,
  normalizeOptionalValue
} from "./SettingsSection.helpers";
import { useSettingsSectionContext } from "./SettingsSectionContext";

export function PdfExportPanel() {
  const {
    pdfExportStatus,
    pdfExportFormState,
    setPdfExportFormState,
    hasDirtyPdfExportSettings,
    savePdfExportSettings,
    isBusy,
    openLibreOfficeDownload
  } = useSettingsSectionContext();

  return (
        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Export PDF</h3>
            <span className={`status-chip status-chip--${pdfExportStatus?.isAvailable ? "online" : "offline"}`}>
              {pdfExportStatus?.isAvailable ? "Disponibile" : "Non installato"}
            </span>
          </div>
          <div className="settings-form">
            <label className="field-group" htmlFor="libreoffice-path">
              <SettingsFieldLabel
                text="Percorso LibreOffice"
                tooltip="Percorso opzionale di soffice.exe usato per esportare traduzioni in PDF."
              />
              <input
                id="libreoffice-path"
                type="text"
                value={pdfExportFormState.libreOfficePath ?? ""}
                title="Percorso opzionale di soffice.exe usato per esportare traduzioni in PDF."
                aria-label="Percorso LibreOffice"
                onChange={(event) =>
                  setPdfExportFormState((current) => ({
                    ...current,
                    libreOfficePath: normalizeOptionalValue(event.target.value)
                  }))
                }
                placeholder="C:\Program Files\LibreOffice\program\soffice.exe"
              />
            </label>
            <SettingsRangeField
              id="pdf-export-timeout"
              label="Timeout export"
              tooltip="Tempo massimo concesso a LibreOffice per generare un PDF."
              min={10}
              max={900}
              step={10}
              value={pdfExportFormState.conversionTimeoutSeconds}
              formatValue={(value) => `${value.toLocaleString("it-IT")} s`}
              onChange={(value) =>
                setPdfExportFormState((current) => ({ ...current, conversionTimeoutSeconds: value }))
              }
            />
            <div className="settings-actions">
              <button type="button" onClick={savePdfExportSettings} disabled={isBusy || !hasDirtyPdfExportSettings}>
                Salva
              </button>
              {pdfExportStatus && !pdfExportStatus.isAvailable && (
                <button type="button" className="button-secondary" onClick={openLibreOfficeDownload} disabled={isBusy}>
                  Scarica LibreOffice
                </button>
              )}
              {hasDirtyPdfExportSettings && <span className="dirty-hint">Modifiche non salvate</span>}
            </div>
            {pdfExportStatus?.executablePath && (
              <div className="panel-note panel-note--path">
                <p title={pdfExportStatus.executablePath} aria-label={`Percorso rilevato: ${pdfExportStatus.executablePath}`}>
                  {pdfExportStatus.executablePath}
                </p>
              </div>
            )}
            {pdfExportStatus?.suggestion && (
              <div className="panel-note panel-note--warning" role="alert">
                <p>{pdfExportStatus.suggestion}</p>
              </div>
            )}
          </div>
        </div>
  );
}

