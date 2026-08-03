import { useRef, useState } from "react";
import type { OcrLanguage, OcrPolicy } from "../../api";
import { useModalFocusTrap } from "../common/useModalFocusTrap";
import { InfoTip } from "../common/InfoTip";
import { getPreferredOcrLanguage } from "./DocumentsSection.formatting";

export function OcrChoiceDialog({
  fileCount,
  languages,
  defaultLanguage,
  onChoice
}: {
  fileCount: number;
  languages: OcrLanguage[];
  defaultLanguage: string;
  onChoice: (policy: OcrPolicy | "cancel", ocrLanguage?: string) => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getPreferredOcrLanguage(languages, defaultLanguage));
  useModalFocusTrap(dialogRef, true, { onEscape: () => onChoice("cancel") });

  return (
    <div className="modal-backdrop">
      <div
        className="ocr-choice-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Scegli modalità OCR"
        ref={dialogRef}
        tabIndex={-1}
      >
        <h3>Modalità di lettura testo</h3>
        <p>
          {fileCount === 1
            ? "Il file selezionato potrebbe essere una scansione o un PDF con immagini."
            : `${fileCount} file selezionati contengono PDF o immagini.`}
          {" "}Come vuoi estrarre il testo?
        </p>

        <OcrLanguageSelect
          languages={languages}
          value={selectedLanguage}
          onChange={setSelectedLanguage}
        />
        <div className="dialog-secondary-note">
          La scelta viene ricordata per il prossimo OCR.
          <InfoTip label="Dettagli sulla lingua OCR">La lingua selezionata viene usata per questo import e proposta alla prossima operazione OCR.</InfoTip>
        </div>

        <div className="ocr-choice-options">
          <button
            className="ocr-choice-option"
            type="button"
            onClick={() => onChoice("Auto", selectedLanguage)}
          >
            <strong>Usa testo esistente</strong>
            <span>Legge il testo già incorporato nel file; usa OCR solo sulle pagine che lo richiedono.</span>
            <em>Consigliato per documenti con testo digitale.</em>
          </button>

          <button
            className="ocr-choice-option"
            type="button"
            onClick={() => onChoice("ForceAll", selectedLanguage)}
          >
            <strong>Rileggi tutto con OCR</strong>
            <span>Tratta ogni pagina come immagine e applica OCR completo, anche se contiene già testo.</span>
            <em>Consigliato per scansioni, documenti stampati o PDF protetti.</em>
          </button>
        </div>

        <div className="settings-actions" style={{ justifyContent: "flex-end" }}>
          <button className="button-secondary" type="button" onClick={() => onChoice("cancel")}>
            Annulla importazione
          </button>
        </div>
      </div>
    </div>
  );
}

export function OcrLanguageDialog({
  documentName,
  actionLabel,
  languages,
  defaultLanguage,
  onChoice
}: {
  documentName: string;
  actionLabel: string;
  languages: OcrLanguage[];
  defaultLanguage: string;
  onChoice: (language: string | "cancel") => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getPreferredOcrLanguage(languages, defaultLanguage));
  useModalFocusTrap(dialogRef, true, { onEscape: () => onChoice("cancel") });

  return (
    <div className="modal-backdrop">
      <div
        className="ocr-choice-dialog"
        role="dialog"
        aria-modal="true"
        aria-label="Scegli lingua OCR"
        ref={dialogRef}
        tabIndex={-1}
      >
        <h3>Lingua documento</h3>
        <p>
          {actionLabel}: {documentName}
        </p>

        <OcrLanguageSelect
          languages={languages}
          value={selectedLanguage}
          onChange={setSelectedLanguage}
        />
        <div className="dialog-secondary-note">
          La scelta viene ricordata per il prossimo OCR.
          <InfoTip label="Dettagli sulla lingua OCR">La lingua selezionata viene usata per questa operazione e proposta alla prossima operazione OCR.</InfoTip>
        </div>

        <div className="settings-actions" style={{ justifyContent: "flex-end" }}>
          <button className="button-secondary" type="button" onClick={() => onChoice("cancel")}>
            Annulla
          </button>
          <button type="button" onClick={() => onChoice(selectedLanguage)}>
            Avvia
          </button>
        </div>
      </div>
    </div>
  );
}

export function OcrLanguageSelect({
  languages,
  value,
  onChange
}: {
  languages: OcrLanguage[];
  value: string;
  onChange: (language: string) => void;
}) {
  return (
    <label className="field-group" htmlFor="ocr-language">
      <span>Lingua del documento</span>
      <select
        id="ocr-language"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {languages
          .slice()
          .sort((left, right) => left.label.localeCompare(right.label))
          .map((language) => (
            <option key={language.code} value={language.code}>
              {language.label} ({language.code})
            </option>
          ))}
      </select>
    </label>
  );
}
