import type { RefObject } from "react";
import type { TranslationCompare, TranslationUnit } from "../api";
import { formatUnitKind } from "./TranslationSection.helpers";
import type { FeedbackState } from "./TranslationSection.types";

export function TranslationCompareModal({
  compareDialogRef,
  compareData,
  activeCompareUnit,
  activeCompareUnitId,
  editedTranslationText,
  isCompareLoading,
  saveState,
  onClose,
  onSaveCorrection,
  onComparePageChange,
  onActiveUnitChange,
  onEditedTextChange
}: {
  compareDialogRef: RefObject<HTMLDivElement | null>;
  compareData: TranslationCompare | null;
  activeCompareUnit: TranslationUnit | null;
  activeCompareUnitId: number | null;
  editedTranslationText: string;
  isCompareLoading: boolean;
  saveState: FeedbackState;
  onClose: () => void;
  onSaveCorrection: () => void;
  onComparePageChange: (page: number | null) => void;
  onActiveUnitChange: (unitId: number) => void;
  onEditedTextChange: (text: string) => void;
}) {
  return (
    <div className="modal-backdrop">
      <div
        className="compare-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="compare-title"
        ref={compareDialogRef}
        tabIndex={-1}
      >
        <div className="compare-modal__header">
          <div>
            <h3 id="compare-title">Confronto traduzione</h3>
            <span>{compareData?.translation.documentName ?? "Documento"}</span>
          </div>
          <div className="compare-header-actions">
            <button
              type="button"
              disabled={!activeCompareUnit || !editedTranslationText.trim()}
              onClick={onSaveCorrection}
            >
              Salva correzione
            </button>
            <button className="button-secondary" type="button" onClick={onClose}>
              Chiudi
            </button>
          </div>
        </div>

        <div className="compare-toolbar">
          <button
            className="button-secondary"
            type="button"
            disabled={isCompareLoading || !compareData?.previousPage}
            onClick={() => onComparePageChange(compareData?.previousPage ?? null)}
          >
            Pagina precedente
          </button>
          <span>
            Pagina {compareData?.pagePosition ?? 0} di {compareData?.pageCount ?? 0}
          </span>
          <button
            className="button-secondary"
            type="button"
            disabled={isCompareLoading || !compareData?.nextPage}
            onClick={() => onComparePageChange(compareData?.nextPage ?? null)}
          >
            Pagina successiva
          </button>
        </div>

        {isCompareLoading && (
          <div className="empty-state compare-empty" role="status" aria-live="polite">
            Caricamento confronto...
          </div>
        )}

        {!isCompareLoading && compareData && compareData.units.length === 0 && (
          <div className="empty-state compare-empty" role="status">
            <p>Traduzione non ancora disponibile.</p>
          </div>
        )}

        {!isCompareLoading && compareData && compareData.units.length > 0 && (
          <>
            <div className="compare-unit-list" aria-label="Unità tradotte nella pagina">
              {compareData.units.map((unit) => (
                <button
                  key={unit.id}
                  type="button"
                  aria-pressed={unit.id === activeCompareUnitId}
                  className={
                    unit.id === activeCompareUnitId
                      ? "compare-unit-pill compare-unit-pill--active"
                      : "compare-unit-pill"
                  }
                  onClick={() => onActiveUnitChange(unit.id)}
                >
                  {formatUnitKind(unit.unitKind)} {unit.unitIndex + 1}
                </button>
              ))}
            </div>

            {activeCompareUnit && (
              <div className="compare-grid">
                <section className="compare-column compare-column--source">
                  <div className="compare-column__header">
                    <strong>Originale</strong>
                    <span>Unità {activeCompareUnit.unitIndex + 1}</span>
                  </div>
                  <p>{activeCompareUnit.sourceText}</p>
                </section>
                <section className="compare-column compare-column--target">
                  <div className="compare-column__header">
                    <strong>Tradotto</strong>
                    <span>
                      {activeCompareUnit.manuallyEdited
                        ? "Correzione salvata"
                        : activeCompareUnit.translatedText
                          ? "Traduzione disponibile"
                          : "Non ancora disponibile"}
                    </span>
                  </div>
                  {!activeCompareUnit.translatedText && (
                    <div className="panel-note panel-note--warning" role="alert">
                      <p>Traduzione non ancora disponibile per questa unità.</p>
                    </div>
                  )}
                  <textarea
                    aria-label="Testo tradotto corretto"
                    value={editedTranslationText}
                    onChange={(event) => onEditedTextChange(event.target.value)}
                    placeholder="Scrivi qui la traduzione corretta"
                  />
                  {activeCompareUnit.machineTranslatedText
                    && activeCompareUnit.manuallyEdited
                    && activeCompareUnit.machineTranslatedText !== activeCompareUnit.translatedText && (
                      <details className="machine-translation-note">
                        <summary>Traduzione iniziale</summary>
                        <p>{activeCompareUnit.machineTranslatedText}</p>
                      </details>
                    )}
                </section>
              </div>
            )}

            {saveState && (
              <div className="compare-footer">
                <span
                  className={`compare-save-state compare-save-state--${saveState.tone}`}
                  role={saveState.tone === "error" ? "alert" : "status"}
                  aria-live={saveState.tone === "error" ? "assertive" : "polite"}
                >
                  {saveState.message}
                </span>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
