import type { OllamaModel, OllamaStatusResponse } from "../../api";
import { TranslationCompareModal } from "./TranslationCompareModal";
import {
  TranslationDetailsPanel,
  TranslationListCard,
  TranslationStartCard
} from "./TranslationSection.views";
import { useTranslationSectionController } from "./useTranslationSectionController";

type TranslationSectionProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  ollamaStatus: OllamaStatusResponse | null;
  loadError: string | null;
};

export function TranslationSection({
  models,
  defaultModel,
  ollamaStatus,
  loadError
}: TranslationSectionProps) {
  const controller = useTranslationSectionController({ models, defaultModel, ollamaStatus });

  return (
    <div className="documents-panel">
      {controller.feedback && (
        <div
          className={`feedback-banner feedback-banner--${controller.feedback.tone}`}
          role={controller.feedback.tone === "error" ? "alert" : "status"}
        >
          {controller.feedback.message}
        </div>
      )}

      <div className="documents-layout">
        <TranslationStartCard
          documents={controller.documents}
          selectedDocumentId={controller.selectedDocumentId}
          selectedDocument={controller.selectedDocument}
          selectedLanguage={controller.selectedLanguage}
          selectedModel={controller.selectedModel}
          models={models}
          ollamaStatus={ollamaStatus}
          loadError={loadError}
          isStarting={controller.isStarting}
          canStart={controller.canStart}
          onDocumentChange={controller.handleDocumentChange}
          onLanguageChange={controller.setSelectedLanguage}
          onModelChange={controller.setSelectedModel}
          onStartTranslation={() => void controller.startTranslation()}
        />

        <TranslationListCard
          translations={controller.translations}
          selectedTranslationId={controller.selectedTranslationId}
          detailsPanelRef={controller.detailsPanelRef}
          onSelectTranslation={controller.setSelectedTranslationId}
          onOpenCompare={(translationId) => controller.openCompare(translationId)}
        />
      </div>

      {controller.selectedTranslation && (
        <TranslationDetailsPanel
          selectedTranslation={controller.selectedTranslation}
          detailsPanelRef={controller.detailsPanelRef}
          exportFormat={controller.exportFormat}
          isExporting={controller.isExporting}
          lastExportPath={controller.lastExportPath}
          onExportFormatChange={controller.setExportFormat}
          onExportTranslation={() => void controller.exportTranslation()}
          onOpenExportFolder={() => void controller.openExportFolder()}
          onOpenCompare={(translationId) => controller.openCompare(translationId)}
        />
      )}

      {controller.compareTranslationId && (
        <TranslationCompareModal
          compareDialogRef={controller.compareDialogRef}
          compareData={controller.compareData}
          activeCompareUnit={controller.activeCompareUnit}
          activeCompareUnitId={controller.activeCompareUnitId}
          editedTranslationText={controller.editedTranslationText}
          isCompareLoading={controller.isCompareLoading}
          saveState={controller.saveState}
          onClose={controller.closeCompare}
          onSaveCorrection={() => void controller.saveCorrection()}
          onComparePageChange={controller.setComparePage}
          onActiveUnitChange={controller.setActiveCompareUnitId}
          onEditedTextChange={controller.setEditedTranslationText}
        />
      )}
    </div>
  );
}
