import type { RefObject } from "react";
import type {
  ImportedDocument,
  OllamaModel,
  OllamaStatusResponse,
  TranslationDetail,
  TranslationSummary
} from "../../api";
import { formatDateTime } from "../../pollingStatus";
import { ProgressBar } from "../common/ProgressBar";
import {
  formatTargetLanguageLabel,
  formatTranslationStatus,
  formatUnitKind,
  targetLanguageOptions
} from "./TranslationSection.helpers";
import type { ExportFormat } from "./TranslationSection.types";

export function TranslationStartCard({
  documents,
  selectedDocumentId,
  selectedDocument,
  selectedLanguage,
  selectedModel,
  models,
  ollamaStatus,
  loadError,
  isStarting,
  canStart,
  onDocumentChange,
  onLanguageChange,
  onModelChange,
  onStartTranslation
}: {
  documents: ImportedDocument[];
  selectedDocumentId: number | null;
  selectedDocument: ImportedDocument | null;
  selectedLanguage: string;
  selectedModel: string;
  models: OllamaModel[];
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
  isStarting: boolean;
  canStart: boolean;
  onDocumentChange: (id: number | null) => void;
  onLanguageChange: (language: string) => void;
  onModelChange: (model: string) => void;
  onStartTranslation: () => void;
}) {
  return (
    <div className="settings-card">
      <div className="settings-card__header">
        <h3>Nuova traduzione</h3>
        <span>{documents.length} documenti</span>
      </div>
      <label className="field-group" htmlFor="translation-document">
        <span>Documento</span>
        <select
          id="translation-document"
          value={selectedDocumentId ?? ""}
          onChange={(event) => {
            const value = Number(event.target.value);
            onDocumentChange(Number.isFinite(value) && value > 0 ? value : null);
          }}
        >
          <option value="">Seleziona documento</option>
          {documents.map((document) => (
            <option key={document.id} value={document.id}>
              {document.originalFileName}
            </option>
          ))}
        </select>
      </label>
      <label className="field-group" htmlFor="translation-language">
        <span>Traduci in</span>
        <select
          id="translation-language"
          value={selectedLanguage}
          onChange={(event) => onLanguageChange(event.target.value)}
        >
          {targetLanguageOptions.map((language) => (
            <option key={language.value} value={language.value}>
              {language.label}
            </option>
          ))}
        </select>
      </label>
      <label className="field-group" htmlFor="translation-model">
        <span>Modello Ollama</span>
        <select
          id="translation-model"
          value={selectedModel}
          onChange={(event) => onModelChange(event.target.value)}
          disabled={!ollamaStatus?.isReachable || models.length === 0}
        >
          <option value="">Seleziona un modello disponibile</option>
          {models.map((model) => (
            <option key={model.name} value={model.name}>
              {model.name}
            </option>
          ))}
        </select>
      </label>
      <div className="settings-actions">
        <button type="button" disabled={!canStart} onClick={onStartTranslation}>
          {isStarting ? "Avvio..." : "Traduci"}
        </button>
      </div>
      <div
        className={selectedDocument?.chunkCount ? "panel-note" : "panel-note panel-note--warning"}
        role={selectedDocument?.chunkCount ? undefined : "alert"}
      >
        {!ollamaStatus?.isReachable && <p>{loadError ?? "Ollama è offline."}</p>}
        {ollamaStatus?.isReachable && models.length === 0 && (
          <p>Installa almeno un modello in Ollama prima di tradurre.</p>
        )}
        {selectedDocument && selectedDocument.pageCount === 0 && (
          <p>Il documento selezionato non ha unità indicizzate da tradurre.</p>
        )}
      </div>
    </div>
  );
}

export function TranslationListCard({
  translations,
  selectedTranslationId,
  detailsPanelRef,
  onSelectTranslation,
  onOpenCompare
}: {
  translations: TranslationSummary[];
  selectedTranslationId: number | null;
  detailsPanelRef: RefObject<HTMLDivElement | null>;
  onSelectTranslation: (translationId: number) => void;
  onOpenCompare: (translationId: number) => void;
}) {
  return (
    <div className="settings-card">
      <div className="settings-card__header">
        <h3>Traduzioni esistenti</h3>
        <span>{translations.length}</span>
      </div>
      {translations.length === 0 ? (
        <div className="empty-state" role="status">
          <p>Nessuna traduzione per il documento selezionato.</p>
        </div>
      ) : (
        <div className="jobs-list" aria-label="Traduzioni esistenti">
          {translations.map((translation) => (
            <article className="job-row" key={translation.id}>
              <div className="job-row__header">
                <div>
                  <strong>{formatTargetLanguageLabel(translation.targetLanguage)}</strong>
                  <span>{translation.model}</span>
                </div>
                <span className={`job-status job-status--${translation.status.toLowerCase()}`}>
                  {formatTranslationStatus(translation.status)}
                </span>
              </div>
              {(translation.status === "Running" || translation.status === "Queued") && (
                <ProgressBar
                  label={`Avanzamento ${translation.progressPercent}%`}
                  value={translation.progressPercent}
                />
              )}
              <div className="job-row__meta">
                <span>{translation.completedUnitCount}/{translation.unitCount} unità</span>
                <span>{formatDateTime(translation.updatedAtUtc)}</span>
              </div>
              {translation.lastError && <p className="job-error-message">{translation.lastError}</p>}
              <div className="settings-actions">
                <button
                  className={`button-secondary${selectedTranslationId === translation.id ? " button-secondary--active" : ""}`}
                  type="button"
                  aria-label={`Mostra dettagli traduzione ${formatTargetLanguageLabel(translation.targetLanguage)} per ${translation.documentName}`}
                  aria-pressed={selectedTranslationId === translation.id}
                  onClick={() => {
                    onSelectTranslation(translation.id);
                    setTimeout(() => detailsPanelRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }), 50);
                  }}
                >
                  Dettagli
                </button>
                <button
                  className="button-secondary"
                  type="button"
                  aria-label={`Apri confronto traduzione ${formatTargetLanguageLabel(translation.targetLanguage)} per ${translation.documentName}`}
                  onClick={() => onOpenCompare(translation.id)}
                >
                  Apri confronto
                </button>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  );
}

export function TranslationDetailsPanel({
  selectedTranslation,
  detailsPanelRef,
  exportFormat,
  isExporting,
  lastExportPath,
  onExportFormatChange,
  onExportTranslation,
  onOpenExportFolder,
  onOpenCompare
}: {
  selectedTranslation: TranslationDetail;
  detailsPanelRef: RefObject<HTMLDivElement | null>;
  exportFormat: ExportFormat;
  isExporting: boolean;
  lastExportPath: string | null;
  onExportFormatChange: (format: ExportFormat) => void;
  onExportTranslation: () => void;
  onOpenExportFolder: () => void;
  onOpenCompare: (translationId: number) => void;
}) {
  return (
    <div ref={detailsPanelRef} className="settings-card document-search-panel">
      <div className="settings-card__header">
        <h3>Unità tradotte</h3>
        <span>{selectedTranslation.translation.progressPercent}%</span>
      </div>
      <div className="settings-actions">
        <label className="inline-field" htmlFor="translation-export-format">
          <span>Formato</span>
          <select
            id="translation-export-format"
            value={exportFormat}
            onChange={(event) => onExportFormatChange(event.target.value as ExportFormat)}
            disabled={isExporting}
          >
            <option value="markdown">Markdown</option>
            <option value="html">HTML</option>
            <option value="txt">TXT</option>
            <option value="docx">DOCX</option>
            <option value="pdf">PDF (richiede LibreOffice)</option>
          </select>
        </label>
        <button
          className="button-secondary"
          type="button"
          disabled={isExporting}
          onClick={onExportTranslation}
        >
          {isExporting ? "Esportazione..." : "Esporta"}
        </button>
        {lastExportPath && (
          <button
            className="button-secondary"
            type="button"
            onClick={onOpenExportFolder}
          >
            Apri cartella
          </button>
        )}
        <button
          className="button-secondary"
          type="button"
          onClick={() => onOpenCompare(selectedTranslation.translation.id)}
        >
          Apri confronto
        </button>
      </div>
      <div className="search-result-list">
        {selectedTranslation.units.slice(0, 80).map((unit) => (
          <article className="search-result-row" key={unit.id}>
            <div className="search-result-row__header">
              <strong>{formatUnitKind(unit.unitKind)} {unit.pageNumber ?? ""}</strong>
              <span>{formatTranslationStatus(unit.status)}</span>
            </div>
            <p>{unit.translatedText ?? unit.sourceText}</p>
            {unit.validationWarnings && <p className="job-error-message">{unit.validationWarnings}</p>}
            {unit.error && <p className="job-error-message">{unit.error}</p>}
          </article>
        ))}
      </div>
    </div>
  );
}
