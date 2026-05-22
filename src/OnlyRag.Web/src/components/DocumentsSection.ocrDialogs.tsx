import { useRef, useState } from "react";
import type { OcrLanguage, OcrPolicy } from "../api";
import { useModalFocusTrap } from "./useModalFocusTrap";
import { getDefaultOcrLanguage } from "./DocumentsSection.formatting";

export function OcrChoiceDialog({
  fileCount,
  languages,
  onChoice
}: {
  fileCount: number;
  languages: OcrLanguage[];
  onChoice: (policy: OcrPolicy | "cancel", ocrLanguage?: string) => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getDefaultOcrLanguage(languages));
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
  onChoice
}: {
  documentName: string;
  actionLabel: string;
  languages: OcrLanguage[];
  onChoice: (language: string | "cancel") => void;
}) {
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState(getDefaultOcrLanguage(languages));
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
  const grouped = languages.reduce<Record<string, OcrLanguage[]>>((groups, language) => {
    const group = language.scriptGroup || "Avanzate";
    groups[group] = [...(groups[group] ?? []), language];
    return groups;
  }, {});

  const groupNames = Object.keys(grouped).sort((left, right) => {
    if (left === "Principali") return -1;
    if (right === "Principali") return 1;
    return left.localeCompare(right);
  });

  return (
    <label className="field-group" htmlFor="ocr-language">
      <span>Lingua documento</span>
      <select
        id="ocr-language"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {groupNames.map((groupName) => (
          <optgroup key={groupName} label={groupName}>
            {grouped[groupName]
              .slice()
              .sort((left, right) => left.label.localeCompare(right.label))
              .map((language) => (
                <option key={language.code} value={language.code}>
                  {language.code} - {language.label}
                </option>
              ))}
          </optgroup>
        ))}
      </select>
    </label>
  );
}
