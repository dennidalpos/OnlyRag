import { useCallback, useEffect, useState } from "react";
import type { AgentStepEvent, OllamaModel } from "../../api";
import type { CodingMode, FileAction } from "./CodingSection.types";
import { useAgentStreamHandler } from "./useAgentStreamHandler";
import { useWorkspaceManager } from "./useWorkspaceManager";

export type CodingMessage = {
  id: string;
  sender: "user" | "assistant";
  content: string;
  timestamp: string;
  modifiedFiles?: string[];
  fileActions?: FileAction[];
  attachedFile?: string;
  isStreaming?: boolean;
  agentEvents?: AgentStepEvent[];
};

export type UseCodingSectionControllerOptions = {
  models: OllamaModel[];
  defaultModel: string | null;
};

export function useCodingSectionController({
  models,
  defaultModel
}: UseCodingSectionControllerOptions) {
  const [selectedModel, setSelectedModel] = useState<string>("");
  const [operatingMode, setOperatingMode] = useState<CodingMode>("full");
  const [autoApproveCommands, setAutoApproveCommands] = useState(false);
  const [isUserScrolledUp, setIsUserScrolledUp] = useState<boolean>(false);

  const workspace = useWorkspaceManager();

  const agentStream = useAgentStreamHandler({
    selectedModel,
    operatingMode,
    autoApproveCommands,
    workspaceConfig: workspace.workspaceConfig,
    workspaceFiles: workspace.workspaceFiles,
    selectedWorkspaceFile: workspace.selectedWorkspaceFile,
    fetchWorkspaceFiles: workspace.fetchWorkspaceFiles,
    handleApplyCodeToFileSilently: workspace.handleApplyCodeToFileSilently,
    handleDeleteWorkspaceFileSilently: workspace.handleDeleteWorkspaceFileSilently,
    setWorkspaceStatusMessage: workspace.setWorkspaceStatusMessage
  });

  useEffect(() => {
    if (defaultModel) {
      setSelectedModel(defaultModel);
    } else if (models.length > 0) {
      setSelectedModel(models[0].name);
    }
  }, [defaultModel, models]);

  const handleScrollContainer = useCallback(() => {
    if (!agentStream.chatContainerRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = agentStream.chatContainerRef.current;
    const distanceFromBottom = scrollHeight - scrollTop - clientHeight;
    setIsUserScrolledUp(distanceFromBottom > 80);
  }, [agentStream.chatContainerRef]);

  const scrollToBottom = useCallback(() => {
    if (agentStream.chatContainerRef.current) {
      agentStream.chatContainerRef.current.scrollTo({
        top: agentStream.chatContainerRef.current.scrollHeight,
        behavior: "smooth"
      });
      setIsUserScrolledUp(false);
    }
  }, [agentStream.chatContainerRef]);

  useEffect(() => {
    if (!isUserScrolledUp && agentStream.chatContainerRef.current) {
      agentStream.chatContainerRef.current.scrollTop = agentStream.chatContainerRef.current.scrollHeight;
    }
  }, [agentStream.messages, agentStream.isGenerating, isUserScrolledUp, agentStream.chatContainerRef]);

  async function handleSendMessage(overridePrompt?: string) {
    let textToSend = overridePrompt ?? (agentStream.promptInput || agentStream.promptInputRef.current);
    if (!textToSend.trim() || agentStream.isGenerating) return;

    if (workspace.singleFiles.length > 0) {
      const filesContext = workspace.singleFiles
        .map((f) => `--- FILE ALLEGATO: ${f.name} (${(f.sizeBytes / 1024).toFixed(1)} KB) ---\n${f.content}`)
        .join("\n\n");
      textToSend = `[CONTESTO FILE ALLEGATI AD-HOC:\n${filesContext}\n]\n\n${textToSend}`;
    }

    return agentStream.handleSendAgentMessage(textToSend);
  }

  function handleExecuteWorkspaceCommandWrapper(cmdToRun?: string) {
    void workspace.handleExecuteWorkspaceCommand(cmdToRun, (logMsg) => {
      agentStream.setMessages((prev) => [
        ...prev,
        {
          id: `cmd_${Date.now()}`,
          sender: "assistant",
          content: logMsg,
          timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
        }
      ]);
    });
  }

  return {
    selectedModel,
    setSelectedModel,
    operatingMode,
    setOperatingMode,
    autoApproveCommands,
    setAutoApproveCommands,
    promptInput: agentStream.promptInput,
    setPromptInput: agentStream.setPromptInput,
    messages: agentStream.messages,
    isGenerating: agentStream.isGenerating,
    error: agentStream.error,
    workspaceConfig: workspace.workspaceConfig,
    workspaceFiles: workspace.workspaceFiles,
    selectedWorkspaceFile: workspace.selectedWorkspaceFile,
    setSelectedWorkspaceFile: workspace.setSelectedWorkspaceFile,
    attachedFileContent: workspace.attachedFileContent,
    setAttachedFileContent: workspace.setAttachedFileContent,
    singleFiles: workspace.singleFiles,
    isWorkspaceFilePickerOpen: workspace.isWorkspaceFilePickerOpen,
    setIsWorkspaceFilePickerOpen: workspace.setIsWorkspaceFilePickerOpen,
    isAttachedFileEditorOpen: workspace.isAttachedFileEditorOpen,
    setIsAttachedFileEditorOpen: workspace.setIsAttachedFileEditorOpen,
    workspaceStatusMessage: workspace.workspaceStatusMessage,
    diffModalFile: workspace.diffModalFile,
    setDiffModalFile: workspace.setDiffModalFile,
    diffModalOriginalContent: workspace.diffModalOriginalContent,
    diffModalModifiedContent: workspace.diffModalModifiedContent,
    diffModalFileApplied: workspace.diffModalFileApplied,
    chatContainerRef: agentStream.chatContainerRef,
    isUserScrolledUp,
    handleScrollContainer,
    scrollToBottom,
    handlePickWindowsFolder: workspace.handlePickWindowsFolder,
    handleClearWorkspaceFolder: workspace.handleClearWorkspaceFolder,
    handleAttachWorkspaceFile: workspace.handleAttachWorkspaceFile,
    handleSaveAttachedFileContent: workspace.handleSaveAttachedFileContent,
    handleOpenExternalFile: workspace.handleOpenExternalFile,
    handleCancelGeneration: agentStream.handleCancelGeneration,
    handleClearMessages: agentStream.handleClearMessages,
    handleOpenDiff: workspace.handleOpenDiff,
    handleApplyCodeToFile: workspace.handleApplyCodeToFile,
    handleRollbackFileContent: workspace.handleRollbackFileContent,
    handleDeleteWorkspaceFile: workspace.handleDeleteWorkspaceFile,
    handleApproveAgentToolCall: agentStream.handleApproveAgentToolCall,
    handleExecuteWorkspaceCommand: handleExecuteWorkspaceCommandWrapper,
    handleAddSingleFiles: workspace.handleAddSingleFiles,
    handleImportFileList: workspace.handleImportFileList,
    handleRemoveSingleFile: workspace.handleRemoveSingleFile,
    handleClearSingleFiles: workspace.handleClearSingleFiles,
    handleSendMessage
  };
}
