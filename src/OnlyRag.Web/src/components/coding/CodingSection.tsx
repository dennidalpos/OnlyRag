import type { OllamaModel } from "../../api";
import { CodingMessageList } from "./CodingMessageList";
import { CodingPromptBar } from "./CodingPromptBar";
import { CodingToolbar } from "./CodingToolbar";
import { MultiAgentSection } from "./MultiAgentSection";
import { AttachedFileEditorModal, DiffViewerModal, WorkspaceFilePickerModal } from "./CodingSection.views";
import { useCodingSectionController } from "./useCodingSectionController";

type CodingSectionProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  loadError?: string | null;
  isActive?: boolean;
};

export function CodingSection({
  models,
  defaultModel,
  loadError = null,
  isActive: _isActive = true
}: CodingSectionProps) {
  const ctrl = useCodingSectionController({ models, defaultModel });

  return (
    <section className="coding-section" aria-label="Coding e Vibe Hub">
      {/* TOOLBAR */}
      <CodingToolbar
        models={models}
        defaultModel={defaultModel}
        selectedModel={ctrl.selectedModel}
        onSelectModel={ctrl.setSelectedModel}
        workspaceConfig={ctrl.workspaceConfig}
        workspaceFilesCount={ctrl.workspaceFiles.length}
        onPickFolder={() => void ctrl.handlePickWindowsFolder()}
        onClearFolder={() => void ctrl.handleClearWorkspaceFolder()}
        onOpenFilePicker={() => ctrl.setIsWorkspaceFilePickerOpen(true)}
      />

      {/* FEEDBACK BANNERS */}
      {ctrl.workspaceStatusMessage && (
        <div className="feedback-banner feedback-banner--info">
          {ctrl.workspaceStatusMessage}
        </div>
      )}

      {loadError && (
        <div className="feedback-banner feedback-banner--warning" role="alert">
          {loadError}
        </div>
      )}

      {ctrl.error && (
        <div className="feedback-banner feedback-banner--error" role="alert">
          {ctrl.error}
        </div>
      )}

      {/* MULTI AGENT ORCHESTRATION */}
      <MultiAgentSection />

      {/* CHAT MESSAGES CONTAINER */}
      <CodingMessageList
        messages={ctrl.messages}
        selectedModel={ctrl.selectedModel}
        chatContainerRef={ctrl.chatContainerRef}
        isUserScrolledUp={ctrl.isUserScrolledUp}
        onScroll={ctrl.handleScrollContainer}
        onScrollToBottom={ctrl.scrollToBottom}
        onApproveAgentToolCall={(callId, approved) =>
          void ctrl.handleApproveAgentToolCall(callId, approved)
        }
        onOpenDiff={(file, code, applied) => void ctrl.handleOpenDiff(file, code, applied)}
        onApplyCodeToFile={(file, code) => void ctrl.handleApplyCodeToFile(file, code)}
        onDeleteWorkspaceFile={(file) => void ctrl.handleDeleteWorkspaceFile(file)}
        onOpenExternalFile={(file) => void ctrl.handleOpenExternalFile(file)}
      />

      {/* PROMPT CONTAINER */}
      <CodingPromptBar
        selectedWorkspaceFile={ctrl.selectedWorkspaceFile}
        attachedFileContent={ctrl.attachedFileContent}
        onOpenAttachedFileEditor={() => ctrl.setIsAttachedFileEditorOpen(true)}
        onOpenWorkspaceFilePicker={() => ctrl.setIsWorkspaceFilePickerOpen(true)}
        onRemoveAttachedFile={() => {
          ctrl.setSelectedWorkspaceFile(null);
          ctrl.setAttachedFileContent(null);
        }}
        promptInput={ctrl.promptInput}
        onPromptInputChange={ctrl.setPromptInput}
        operatingMode={ctrl.operatingMode}
        onSelectOperatingMode={ctrl.setOperatingMode}
        autoApproveCommands={ctrl.autoApproveCommands}
        onToggleAutoApproveCommands={ctrl.setAutoApproveCommands}
        workspaceConfigAuthorized={Boolean(ctrl.workspaceConfig?.isAuthorized)}
        messagesCount={ctrl.messages.length}
        isGenerating={ctrl.isGenerating}
        onSendMessage={() => void ctrl.handleSendMessage()}
        onCancelGeneration={ctrl.handleCancelGeneration}
        onClearMessages={ctrl.handleClearMessages}
      />

      {/* WORKSPACE FILE PICKER MODAL */}
      <WorkspaceFilePickerModal
        isOpen={ctrl.isWorkspaceFilePickerOpen}
        onClose={() => ctrl.setIsWorkspaceFilePickerOpen(false)}
        files={ctrl.workspaceFiles}
        onSelectFile={(relPath) => void ctrl.handleAttachWorkspaceFile(relPath)}
      />

      {/* ATTACHED FILE EDITOR MODAL */}
      <AttachedFileEditorModal
        isOpen={ctrl.isAttachedFileEditorOpen}
        onClose={() => ctrl.setIsAttachedFileEditorOpen(false)}
        fileName={ctrl.selectedWorkspaceFile}
        content={ctrl.attachedFileContent || ""}
        onSaveContent={(updatedContent, saveToDisk) =>
          void ctrl.handleSaveAttachedFileContent(updatedContent, saveToDisk)
        }
      />

      {/* DIFF VIEWER MODAL */}
      <DiffViewerModal
        isOpen={Boolean(ctrl.diffModalFile)}
        fileName={ctrl.diffModalFile}
        originalContent={ctrl.diffModalOriginalContent}
        modifiedContent={ctrl.diffModalModifiedContent}
        isAppliedOnDisk={ctrl.diffModalFileApplied}
        onClose={() => ctrl.setDiffModalFile(null)}
        onSaveToDisk={
          !ctrl.diffModalFileApplied && ctrl.diffModalFile
            ? () => void ctrl.handleApplyCodeToFile(ctrl.diffModalFile!, ctrl.diffModalModifiedContent)
            : undefined
        }
        onRollback={
          ctrl.diffModalFile && ctrl.diffModalOriginalContent
            ? () => void ctrl.handleRollbackFileContent(ctrl.diffModalFile!, ctrl.diffModalOriginalContent)
            : undefined
        }
      />
    </section>
  );
}
