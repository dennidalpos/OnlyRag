import React, { useRef } from "react";
import type { RefObject } from "react";
import type { TranslationCompare, TranslationUnit } from "../../api";
import type { FeedbackState } from "./TranslationSection.types";
import { useModalMaximize } from "../common/useModalMaximize";

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
  const modalSize = useModalMaximize();
  const sourceRef = useRef<HTMLParagraphElement | null>(null);
  const targetRef = useRef<HTMLTextAreaElement | null>(null);
  const isSyncingRef = useRef(false);

  function handleSourceScroll() {
    if (isSyncingRef.current || !sourceRef.current || !targetRef.current) return;
    isSyncingRef.current = true;
    const sourceEl = sourceRef.current;
    const targetEl = targetRef.current;
    const scrollRatio = sourceEl.scrollTop / (sourceEl.scrollHeight - sourceEl.clientHeight || 1);
    targetEl.scrollTop = scrollRatio * (targetEl.scrollHeight - targetEl.clientHeight);
    setTimeout(() => { isSyncingRef.current = false; }, 30);
  }

  function handleTargetScroll() {
    if (isSyncingRef.current || !sourceRef.current || !targetRef.current) return;
    isSyncingRef.current = true;
    const sourceEl = sourceRef.current;
    const targetEl = targetRef.current;
    const scrollRatio = targetEl.scrollTop / (targetEl.scrollHeight - targetEl.clientHeight || 1);
    sourceEl.scrollTop = scrollRatio * (sourceEl.scrollHeight - sourceEl.clientHeight);
    setTimeout(() => { isSyncingRef.current = false; }, 30);
  }

  return (
    <div className="modal-backdrop">
      <div
        className={`compare-modal modal-frame--resizable${modalSize.maximizedClassName}`}
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
            <button className="button-secondary" type="button" onClick={modalSize.toggleMaximized}>
              {modalSize.maximizeLabel}
            </button>
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
                  {unit.displayLabel}
                </button>
              ))}
            </div>

            {activeCompareUnit && (
              (() => {
                const sourceText = activeCompareUnit.sourceText || "";
                const targetText = editedTranslationText || "";

                const sourceWords = sourceText.trim() ? sourceText.trim().split(/\s+/).length : 0;
                const sourceChars = sourceText.length;

                const targetWords = targetText.trim() ? targetText.trim().split(/\s+/).length : 0;
                const targetChars = targetText.length;

                return (
                  <div className="compare-grid">
                    <section className="compare-column compare-column--source">
                      <div className="compare-column__header">
                        <strong>Originale</strong>
                        <span>{activeCompareUnit.displayLabel}</span>
                      </div>
                      <div className="compare-stats-bar" style={{ padding: "4px 12px", background: "#0f172a", fontSize: "0.75rem", color: "#94a3b8", display: "flex", gap: "12px", borderBottom: "1px solid #1e293b" }}>
                        <span><strong>Parole:</strong> {sourceWords}</span>
                        <span><strong>Caratteri:</strong> {sourceChars}</span>
                      </div>
                      <p ref={sourceRef} onScroll={handleSourceScroll} style={{ overflowY: "auto", maxHeight: "300px", padding: "12px" }}>
                        {activeCompareUnit.sourceText}
                      </p>
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
                      <div className="compare-stats-bar" style={{ padding: "4px 12px", background: "#0f172a", fontSize: "0.75rem", color: "#38bdf8", display: "flex", gap: "12px", borderBottom: "1px solid #1e293b" }}>
                        <span><strong>Parole:</strong> {targetWords}</span>
                        <span><strong>Caratteri:</strong> {targetChars}</span>
                      </div>
                      {!activeCompareUnit.translatedText && (
                        <div className="panel-note panel-note--warning" role="alert">
                          <p>Traduzione non ancora disponibile per questa unità.</p>
                        </div>
                      )}
                      <textarea
                        ref={targetRef}
                        onScroll={handleTargetScroll}
                        aria-label="Testo tradotto corretto"
                        value={editedTranslationText}
                        onChange={(event) => onEditedTextChange(event.target.value)}
                        placeholder="Scrivi qui la traduzione corretta"
                        style={{ minHeight: "220px", overflowY: "auto" }}
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
                );
              })()
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
