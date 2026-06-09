import { formatLastRefresh, shouldSurfaceRefreshFailure } from "../pollingStatus";
import { DocumentPreviewModal } from "./DocumentPreviewModal";
import { ProgressBar } from "./ProgressBar";
import {
  DocumentDetailCard,
  DocumentListRow,
  OcrChoiceDialog,
  OcrLanguageDialog
} from "./DocumentsSection.helpers";
import { useDocumentsSectionController } from "./useDocumentsSectionController";

type DocumentsSectionProps = {
  onLibraryChanged?: () => void;
};

export function DocumentsSection({ onLibraryChanged }: DocumentsSectionProps) {
  const {
    busyDocumentId,
    canPreview,
    detailRefreshStatus,
    documentRefreshStatus,
    documents,
    embeddingStatus,
    feedback,
    fileInputRef,
    handleBrowseClick,
    handleClosePreview,
    handleDelete,
    handleDragLeave,
    handleDragOver,
    handleDrop,
    handleEmbed,
    handleInputChange,
    handleOcrActionLanguage,
    handleOcrChoice,
    handleOpenPreview,
    handleReindex,
    handleRunOcr,
    handleSelectDocument,
    isDragActive,
    isLoading,
    isLoadingPreview,
    isUploading,
    loadPreviewPage,
    ocrDefaultLanguage,
    ocrLanguages,
    ocrStatus,
    pendingImport,
    pendingOcrAction,
    pipelineStatus,
    previewData,
    previewDocument,
    selectedDocument,
    selectedJob,
    vectorHealth
  } = useDocumentsSectionController({ onLibraryChanged });

  return (
    <div className="documents-panel">
      {pendingImport && (
        <OcrChoiceDialog
          fileCount={Array.from(pendingImport.files).length}
          languages={ocrLanguages}
          defaultLanguage={ocrDefaultLanguage}
          onChoice={handleOcrChoice}
        />
      )}

      {pendingOcrAction && (
        <OcrLanguageDialog
          documentName={pendingOcrAction.document.originalFileName}
          actionLabel={pendingOcrAction.kind === "reindex"
            ? "Ricostruisci indice"
            : pendingOcrAction.force
              ? "Rileggi tutto con OCR"
              : "Riesegui lettura testo"}
          languages={ocrLanguages}
          defaultLanguage={ocrDefaultLanguage}
          onChoice={handleOcrActionLanguage}
        />
      )}

      {previewDocument && (
        <DocumentPreviewModal
          document={previewDocument}
          preview={previewData}
          isLoading={isLoadingPreview}
          onClose={handleClosePreview}
          onPageChange={(page) => void loadPreviewPage(previewDocument, page)}
        />
      )}

      <div
        className={isDragActive ? "document-dropzone document-dropzone--active" : "document-dropzone"}
        onDragLeave={handleDragLeave}
        onDragOver={handleDragOver}
        onDrop={handleDrop}
      >
        <strong>Trascina qui i file da importare</strong>
        <div className="settings-actions">
          <button disabled={isUploading} type="button" onClick={handleBrowseClick}>
            Sfoglia file
          </button>
        </div>
        {isUploading && (
          <div className="document-dropzone__progress">
            <ProgressBar label="Importazione in corso..." value={0} indeterminate />
          </div>
        )}
        <input
          ref={fileInputRef}
          hidden
          multiple
          type="file"
          accept=".txt,.md,.markdown,.csv,.pdf,.png,.jpg,.jpeg,.tif,.tiff,.bmp,.gif,.webp,.docx,.xlsx,.pptx"
          onChange={handleInputChange}
        />
      </div>

      {vectorHealth?.nearLimit && (
        <div className="panel-note panel-note--warning" role="alert">
          <p>{vectorHealth.warning ?? `Backend vettoriale: ${vectorHealth.totalVectors}/${vectorHealth.vectorLimit} vettori usati.`}</p>
        </div>
      )}

      {feedback && (
        <div
          className={`feedback-banner feedback-banner--${feedback.tone}`}
          role={feedback.tone === "error" ? "alert" : "status"}
        >
          {feedback.message}
        </div>
      )}

      <div className="documents-main-layout">
        <div className="documents-list-card">
          <div className="documents-toolbar">
            <strong>Documenti importati</strong>
            <span>{documents.length}</span>
            <small>Ultimo aggiornamento: {formatLastRefresh(documentRefreshStatus.lastSuccessfulRefreshAt)}</small>
          </div>
          {shouldSurfaceRefreshFailure(documentRefreshStatus) && (
            <div className="feedback-banner feedback-banner--error" role="alert">
              {documentRefreshStatus.lastErrorMessage} Stato non aggiornato da {formatLastRefresh(documentRefreshStatus.lastSuccessfulRefreshAt)}.
            </div>
          )}
          {isLoading ? (
            <div className="empty-state" role="status" aria-live="polite"><p>Caricamento documenti...</p></div>
          ) : documents.length === 0 ? (
            <div className="empty-state" role="status"><p>Nessun documento presente. Importa un file per iniziare.</p></div>
          ) : (
            <div className="documents-list" role="listbox" aria-label="Documenti importati">
              {documents.map((doc) => (
                <DocumentListRow
                  key={doc.id}
                  document={doc}
                  isSelected={selectedDocument?.id === doc.id}
                  isBusy={busyDocumentId === doc.id}
                  onSelect={handleSelectDocument}
                />
              ))}
            </div>
          )}
        </div>

        {selectedDocument && (
          <div className="document-detail-stack">
            {shouldSurfaceRefreshFailure(detailRefreshStatus) && (
              <div className="feedback-banner feedback-banner--error" role="alert">
                {detailRefreshStatus.lastErrorMessage} Dettaglio non aggiornato da {formatLastRefresh(detailRefreshStatus.lastSuccessfulRefreshAt)}.
              </div>
            )}
            <DocumentDetailCard
              document={selectedDocument}
              pipelineStatus={pipelineStatus}
              embeddingStatus={embeddingStatus}
              ocrStatus={ocrStatus}
              activeJob={selectedJob}
              isBusy={busyDocumentId === selectedDocument.id}
              canPreview={canPreview}
              onReindex={handleReindex}
              onEmbed={handleEmbed}
              onOcr={handleRunOcr}
              onDelete={handleDelete}
              onPreview={handleOpenPreview}
            />
          </div>
        )}
      </div>
    </div>
  );
}
