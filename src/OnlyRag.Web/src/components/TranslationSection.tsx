import { useEffect, useMemo, useRef, useState } from "react";
import {
  apiRequest,
  type ImportedDocument,
  type OllamaModel,
  type OllamaStatusResponse,
  type OperationMessageResponse,
  type TranslationCompare,
  type TranslationDetail,
  type TranslationExport,
  type TranslationSummary,
  type TranslationUnit
} from "../api";
import { clearExitContributor, setExitContributor } from "../appLifecycle";
import { ProgressBar } from "./ProgressBar";
import { useModalFocusTrap } from "./useModalFocusTrap";
import { buildCompareDraftKey, formatTranslationStatus, formatUnitKind, targetLanguages } from "./TranslationSection.helpers";

type TranslationSectionProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
};

type FeedbackState = {
  tone: "info" | "error";
  message: string;
} | null;

type ExportFormat = "txt" | "markdown" | "html" | "docx" | "pdf";

export function TranslationSection({
  models,
  defaultModel,
  ollamaStatus,
  loadError
}: TranslationSectionProps) {
  const [documents, setDocuments] = useState<ImportedDocument[]>([]);
  const [selectedDocumentId, setSelectedDocumentId] = useState<number | null>(null);
  const [selectedLanguage, setSelectedLanguage] = useState("English");
  const [selectedModel, setSelectedModel] = useState("");
  const [translations, setTranslations] = useState<TranslationSummary[]>([]);
  const [selectedTranslationId, setSelectedTranslationId] = useState<number | null>(null);
  const [selectedTranslation, setSelectedTranslation] = useState<TranslationDetail | null>(null);
  const [compareTranslationId, setCompareTranslationId] = useState<number | null>(null);
  const [comparePage, setComparePage] = useState<number | null>(null);
  const [compareData, setCompareData] = useState<TranslationCompare | null>(null);
  const [activeCompareUnitId, setActiveCompareUnitId] = useState<number | null>(null);
  const [editedTranslationText, setEditedTranslationText] = useState("");
  const [isCompareLoading, setIsCompareLoading] = useState(false);
  const [saveState, setSaveState] = useState<FeedbackState>(null);
  const [isStarting, setIsStarting] = useState(false);
  const [exportFormat, setExportFormat] = useState<ExportFormat>("markdown");
  const [isExporting, setIsExporting] = useState(false);
  const [lastExportPath, setLastExportPath] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<FeedbackState>(null);
  const compareDialogRef = useRef<HTMLDivElement | null>(null);
  const compareOpenerRef = useRef<HTMLElement | null>(null);
  const detailsPanelRef = useRef<HTMLDivElement | null>(null);

  const selectedDocument = useMemo(
    () => documents.find((document) => document.id === selectedDocumentId) ?? null,
    [documents, selectedDocumentId]
  );

  useEffect(() => {
    const modelNames = models.map((model) => model.name);
    const nextValue =
      defaultModel && modelNames.includes(defaultModel) ? defaultModel : modelNames[0] ?? "";
    setSelectedModel(nextValue);
  }, [defaultModel, models]);

  useEffect(() => {
    let isCancelled = false;

    async function loadDocuments() {
      try {
        const docs = await apiRequest<ImportedDocument[]>("/api/documents");
        if (isCancelled) {
          return;
        }

        setDocuments(docs);
        setSelectedDocumentId((current) => current ?? docs[0]?.id ?? null);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({
            tone: "error",
            message: error instanceof Error ? error.message : "Impossibile leggere i documenti."
          });
        }
      }
    }

    void loadDocuments();

    return () => {
      isCancelled = true;
    };
  }, []);

  useEffect(() => {
    let isCancelled = false;

    async function refreshTranslations() {
      if (!selectedDocumentId) {
        setTranslations([]);
        setSelectedTranslation(null);
        return;
      }

      try {
        const items = await apiRequest<TranslationSummary[]>(
          `/api/documents/${selectedDocumentId}/translations`
        );
        if (isCancelled) {
          return;
        }

        setTranslations(items);
        setSelectedTranslationId((current) => current ?? items[0]?.id ?? null);
      } catch (error) {
        if (!isCancelled) {
          setFeedback({
            tone: "error",
            message: error instanceof Error ? error.message : "Impossibile leggere le traduzioni."
          });
        }
      }
    }

    void refreshTranslations();
    const interval = window.setInterval(() => void refreshTranslations(), 3000);

    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedDocumentId]);

  useEffect(() => {
    let isCancelled = false;

    async function refreshTranslationDetail() {
      if (!selectedTranslationId) {
        setSelectedTranslation(null);
        return;
      }

      try {
        const detail = await apiRequest<TranslationDetail>(`/api/translations/${selectedTranslationId}`);
        if (!isCancelled) {
          setSelectedTranslation(detail);
        }
      } catch {
        if (!isCancelled) {
          setSelectedTranslation(null);
        }
      }
    }

    void refreshTranslationDetail();
    const interval = window.setInterval(() => void refreshTranslationDetail(), 3000);

    return () => {
      isCancelled = true;
      window.clearInterval(interval);
    };
  }, [selectedTranslationId]);

  useEffect(() => {
    let isCancelled = false;

    async function loadCompare() {
      if (!compareTranslationId) {
        setCompareData(null);
        setActiveCompareUnitId(null);
        setEditedTranslationText("");
        setSaveState(null);
        return;
      }

      setIsCompareLoading(true);
      try {
        const suffix = comparePage ? `?page=${comparePage}` : "";
        const data = await apiRequest<TranslationCompare>(
          `/api/translations/${compareTranslationId}/compare${suffix}`
        );
        if (isCancelled) {
          return;
        }

        const firstAvailableUnit =
          data.units.find((unit) => unit.translatedText || unit.machineTranslatedText)
          ?? data.units[0]
          ?? null;
        setCompareData(data);
        setComparePage(data.currentPage);
        setActiveCompareUnitId((current) =>
          current && data.units.some((unit) => unit.id === current)
            ? current
            : firstAvailableUnit?.id ?? null
        );
      } catch (error) {
        if (!isCancelled) {
          setSaveState({
            tone: "error",
            message: error instanceof Error ? error.message : "Confronto non disponibile."
          });
        }
      } finally {
        if (!isCancelled) {
          setIsCompareLoading(false);
        }
      }
    }

    void loadCompare();

    return () => {
      isCancelled = true;
    };
  }, [comparePage, compareTranslationId]);

  const activeCompareUnit = useMemo(() => {
    return compareData?.units.find((unit) => unit.id === activeCompareUnitId) ?? null;
  }, [activeCompareUnitId, compareData]);

  const compareDraftKey = activeCompareUnit
    ? buildCompareDraftKey(compareTranslationId, activeCompareUnit.id)
    : null;

  const hasUnsavedCompareDraft = Boolean(
    activeCompareUnit && editedTranslationText !== (activeCompareUnit.translatedText ?? "")
  );

  useEffect(() => {
    if (!activeCompareUnit) {
      setEditedTranslationText("");
      setSaveState(null);
      return;
    }

    try {
      const savedDraft = compareDraftKey ? window.localStorage.getItem(compareDraftKey) : null;
      setEditedTranslationText(savedDraft ?? activeCompareUnit.translatedText ?? "");
    } catch {
      setEditedTranslationText(activeCompareUnit.translatedText ?? "");
    }

    setSaveState(null);
  }, [activeCompareUnit, compareDraftKey]);

  useEffect(() => {
    if (!compareDraftKey) {
      return;
    }

    try {
      if (!hasUnsavedCompareDraft) {
        window.localStorage.removeItem(compareDraftKey);
      } else {
        window.localStorage.setItem(compareDraftKey, editedTranslationText);
      }
    } catch {
    }
  }, [compareDraftKey, editedTranslationText, hasUnsavedCompareDraft]);

  useEffect(() => {
    setExitContributor("translation", {
      label: "Traduzione",
      hasPendingChanges: hasUnsavedCompareDraft,
      hasActiveWork: isStarting || isExporting || isCompareLoading,
      prepareForExit: persistCompareDraftAsync
    });

    return () => {
      clearExitContributor("translation");
    };
  }, [hasUnsavedCompareDraft, isCompareLoading, isExporting, isStarting, persistCompareDraftAsync]);

  useModalFocusTrap(compareDialogRef, Boolean(compareTranslationId), {
    onEscape: closeCompare,
    restoreFocus: false
  });

  async function startTranslation() {
    if (!selectedDocumentId) {
      setFeedback({ tone: "error", message: "Seleziona un documento." });
      return;
    }

    if (!selectedModel) {
      setFeedback({ tone: "error", message: "Seleziona un modello Ollama installato." });
      return;
    }

    setIsStarting(true);
    setFeedback(null);
    try {
      const detail = await apiRequest<TranslationDetail>("/api/translations", {
        method: "POST",
        body: JSON.stringify({
          documentId: selectedDocumentId,
          targetLanguage: selectedLanguage,
          model: selectedModel
        })
      });
      setSelectedTranslationId(detail.translation.id);
      setSelectedTranslation(detail);
      const items = await apiRequest<TranslationSummary[]>(
        `/api/documents/${selectedDocumentId}/translations`
      );
      setTranslations(items);
      setFeedback({ tone: "info", message: "Traduzione accodata." });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Traduzione non avviata."
      });
    } finally {
      setIsStarting(false);
    }
  }

  async function exportTranslation() {
    if (!selectedTranslationId) {
      return;
    }

    setIsExporting(true);
    setFeedback(null);
    try {
      const exported = await apiRequest<TranslationExport>(
        `/api/translations/${selectedTranslationId}/export`,
        {
          method: "POST",
          body: JSON.stringify({ format: exportFormat })
        }
      );
      setLastExportPath(exported.outputPath);
      setFeedback({ tone: "info", message: `Export completato: ${exported.outputPath}` });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Export non riuscito."
      });
    } finally {
      setIsExporting(false);
    }
  }

  async function openExportFolder() {
    try {
      await apiRequest<OperationMessageResponse>("/api/documents/exports/open-folder", {
        method: "POST",
        body: JSON.stringify({ confirmed: true })
      });
    } catch (error) {
      setFeedback({
        tone: "error",
        message: error instanceof Error ? error.message : "Cartella export non aperta."
      });
    }
  }

  function openCompare(translationId: number, page?: number | null) {
    compareOpenerRef.current = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    setCompareTranslationId(translationId);
    setComparePage(page ?? null);
    setCompareData(null);
    setActiveCompareUnitId(null);
    setEditedTranslationText("");
    setSaveState(null);
  }

  function closeCompare() {
    setCompareTranslationId(null);
    setComparePage(null);
    setCompareData(null);
    setActiveCompareUnitId(null);
    setEditedTranslationText("");
    setSaveState(null);
    compareOpenerRef.current?.focus();
    compareOpenerRef.current = null;
  }

  async function persistCompareDraftAsync() {
    if (!activeCompareUnit) {
      return;
    }

    if (compareDraftKey) {
      try {
        if (hasUnsavedCompareDraft) {
          window.localStorage.setItem(compareDraftKey, editedTranslationText);
        } else {
          window.localStorage.removeItem(compareDraftKey);
        }
      } catch {
      }
    }

    if (!hasUnsavedCompareDraft || !editedTranslationText.trim()) {
      return;
    }

    await saveCorrection(true);
  }

  async function saveCorrection(isSilent = false) {
    if (!compareTranslationId || !activeCompareUnit) {
      return;
    }

    if (!editedTranslationText.trim()) {
      if (!isSilent) {
        setSaveState({ tone: "error", message: "Inserisci il testo tradotto." });
      }
      return;
    }

    if (!isSilent) {
      setSaveState({ tone: "info", message: "Salvataggio..." });
    }
    try {
      const updated = await apiRequest<TranslationUnit>(
        `/api/translations/${compareTranslationId}/units/${activeCompareUnit.id}`,
        {
          method: "PUT",
          body: JSON.stringify({ translatedText: editedTranslationText })
        }
      );
      setCompareData((current) =>
        current
          ? {
              ...current,
              units: current.units.map((unit) => (unit.id === updated.id ? updated : unit))
            }
          : current
      );
      setSelectedTranslation((current) =>
        current && current.translation.id === compareTranslationId
          ? {
              ...current,
              units: current.units.map((unit) => (unit.id === updated.id ? updated : unit))
            }
          : current
      );
      if (compareDraftKey) {
        try {
          window.localStorage.removeItem(compareDraftKey);
        } catch {
        }
      }

      if (!isSilent) {
        setSaveState({ tone: "info", message: "Correzione salvata." });
      }
    } catch (error) {
      if (!isSilent) {
        setSaveState({
          tone: "error",
          message: error instanceof Error ? error.message : "Salvataggio non riuscito."
        });
      }

      throw error;
    }
  }

  const canStart =
    Boolean(ollamaStatus?.isReachable)
    && models.length > 0
    && Boolean(selectedDocumentId)
    && Boolean(selectedModel)
    && !isStarting;

  return (
    <div className="documents-panel">
      {feedback && (
        <div
          className={`feedback-banner feedback-banner--${feedback.tone}`}
          role={feedback.tone === "error" ? "alert" : "status"}
        >
          {feedback.message}
        </div>
      )}

      <div className="documents-layout">
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
                setSelectedDocumentId(Number.isFinite(value) && value > 0 ? value : null);
                setSelectedTranslationId(null);
                setSelectedTranslation(null);
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
            <span>Lingua target</span>
            <select
              id="translation-language"
              value={selectedLanguage}
              onChange={(event) => setSelectedLanguage(event.target.value)}
            >
              {targetLanguages.map((language) => (
                <option key={language} value={language}>
                  {language}
                </option>
              ))}
            </select>
          </label>
          <label className="field-group" htmlFor="translation-model">
            <span>Modello Ollama</span>
            <select
              id="translation-model"
              value={selectedModel}
              onChange={(event) => setSelectedModel(event.target.value)}
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
            <button type="button" disabled={!canStart} onClick={() => void startTranslation()}>
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

        <div className="settings-card">
          <div className="settings-card__header">
            <h3>Traduzioni esistenti</h3>
            <span>{translations.length}</span>
          </div>
          {translations.length === 0 ? (
            <div className="empty-state">
              <p>Nessuna traduzione per il documento selezionato.</p>
            </div>
          ) : (
            <div className="jobs-list">
              {translations.map((translation) => (
                <article className="job-row" key={translation.id}>
                  <div className="job-row__header">
                    <div>
                      <strong>{translation.targetLanguage}</strong>
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
                    <span>{new Date(translation.updatedAtUtc).toLocaleString()}</span>
                  </div>
                  {translation.lastError && <p className="job-error-message">{translation.lastError}</p>}
                  <div className="settings-actions">
                    <button
                      className={`button-secondary${selectedTranslationId === translation.id ? " button-secondary--active" : ""}`}
                      type="button"
                      onClick={() => {
                        setSelectedTranslationId(translation.id);
                        setTimeout(() => detailsPanelRef.current?.scrollIntoView({ behavior: "smooth", block: "start" }), 50);
                      }}
                    >
                      Dettagli
                    </button>
                    <button
                      className="button-secondary"
                      type="button"
                      onClick={() => openCompare(translation.id)}
                    >
                      Apri confronto
                    </button>
                  </div>
                </article>
              ))}
            </div>
          )}
        </div>
      </div>

      {selectedTranslation && (
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
                onChange={(event) => setExportFormat(event.target.value as ExportFormat)}
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
              onClick={() => void exportTranslation()}
            >
              {isExporting ? "Export..." : "Esporta"}
            </button>
            {lastExportPath && (
              <button
                className="button-secondary"
                type="button"
                onClick={() => void openExportFolder()}
              >
                Apri cartella
              </button>
            )}
            <button
              className="button-secondary"
              type="button"
              onClick={() => openCompare(selectedTranslation.translation.id)}
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
      )}

      {compareTranslationId && (
        <div
          className="modal-backdrop"
          role="dialog"
          aria-modal="true"
          aria-labelledby="compare-title"
          ref={compareDialogRef}
          tabIndex={-1}
        >
          <div className="compare-modal">
            <div className="compare-modal__header">
              <div>
                <h3 id="compare-title">Confronto traduzione</h3>
                <span>{compareData?.translation.documentName ?? "Documento"}</span>
              </div>
              <div className="compare-header-actions">
                <button
                  type="button"
                  disabled={!activeCompareUnit || !editedTranslationText.trim()}
                  onClick={() => void saveCorrection()}
                >
                  Salva correzione
                </button>
                <button className="button-secondary" type="button" onClick={closeCompare}>
                  Chiudi
                </button>
              </div>
            </div>

            <div className="compare-toolbar">
              <button
                className="button-secondary"
                type="button"
                disabled={isCompareLoading || !compareData?.previousPage}
                onClick={() => setComparePage(compareData?.previousPage ?? null)}
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
                onClick={() => setComparePage(compareData?.nextPage ?? null)}
              >
                Pagina successiva
              </button>
            </div>

            {isCompareLoading && <div className="empty-state compare-empty">Caricamento confronto...</div>}

            {!isCompareLoading && compareData && compareData.units.length === 0 && (
              <div className="empty-state compare-empty">
                <p>Traduzione non ancora disponibile.</p>
              </div>
            )}

            {!isCompareLoading && compareData && compareData.units.length > 0 && (
              <>
                <div className="compare-unit-list">
                  {compareData.units.map((unit) => (
                    <button
                      key={unit.id}
                      type="button"
                      className={
                        unit.id === activeCompareUnitId
                          ? "compare-unit-pill compare-unit-pill--active"
                          : "compare-unit-pill"
                      }
                      onClick={() => setActiveCompareUnitId(unit.id)}
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
                          <p>Traduzione non ancora disponibile per questa unita.</p>
                        </div>
                      )}
                      <textarea
                        aria-label="Testo tradotto corretto"
                        value={editedTranslationText}
                        onChange={(event) => setEditedTranslationText(event.target.value)}
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
                    <span className={`compare-save-state compare-save-state--${saveState.tone}`}>
                      {saveState.message}
                    </span>
                  </div>
                )}
              </>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

