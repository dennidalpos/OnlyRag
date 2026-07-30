import { useEffect, useRef, useState } from "react";
import {
  apiAgentStreamRequest,
  apiRequest,
  type AgentRunRequest,
  type AgentStepEvent,
  type ApproveToolCallRequest,
  type OllamaModel
} from "../api";
import type {
  DeleteWorkspaceFileRequest,
  DeleteWorkspaceFileResponse,
  ExecuteWorkspaceCommandRequest,
  ExecuteWorkspaceCommandResponse,
  OpenExternalFileRequest,
  PickWorkspaceFolderResponse,
  ReadWorkspaceFileRequest,
  ReadWorkspaceFileResponse,
  WorkspaceConfig,
  WorkspaceFileItem,
  WriteWorkspaceFileRequest,
  WriteWorkspaceFileResponse
} from "../apiTypes";
import type { FileAction } from "./CodingSection.types";

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
  const [operatingMode, setOperatingMode] = useState<"plan" | "write">("write");
  const [autoApproveCommands, setAutoApproveCommands] = useState(false);
  const [promptInput, setPromptInput] = useState("");
  const [messages, setMessages] = useState<CodingMessage[]>([]);
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Workspace state
  const [workspaceConfig, setWorkspaceConfig] = useState<WorkspaceConfig | null>(null);
  const [workspaceFiles, setWorkspaceFiles] = useState<WorkspaceFileItem[]>([]);
  const [selectedWorkspaceFile, setSelectedWorkspaceFile] = useState<string | null>(null);
  const [attachedFileContent, setAttachedFileContent] = useState<string | null>(null);
  const [isWorkspaceFilePickerOpen, setIsWorkspaceFilePickerOpen] = useState(false);
  const [isAttachedFileEditorOpen, setIsAttachedFileEditorOpen] = useState(false);
  const [workspaceStatusMessage, setWorkspaceStatusMessage] = useState<string | null>(null);

  // Diff Modal State
  const [diffModalFile, setDiffModalFile] = useState<string | null>(null);
  const [diffModalOriginalContent, setDiffModalOriginalContent] = useState<string>("");
  const [diffModalModifiedContent, setDiffModalModifiedContent] = useState<string>("");
  const [diffModalFileApplied, setDiffModalFileApplied] = useState<boolean>(false);

  const chatContainerRef = useRef<HTMLDivElement | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  useEffect(() => {
    if (defaultModel) {
      setSelectedModel(defaultModel);
    } else if (models.length > 0) {
      setSelectedModel(models[0].name);
    }
  }, [defaultModel, models]);

  useEffect(() => {
    void refreshWorkspaceConfig();
  }, []);

  useEffect(() => {
    if (chatContainerRef.current) {
      chatContainerRef.current.scrollTop = chatContainerRef.current.scrollHeight;
    }
  }, [messages, isGenerating]);

  // Auto-dismiss workspace status messages after 5 seconds
  useEffect(() => {
    if (!workspaceStatusMessage) return;
    const timer = setTimeout(() => setWorkspaceStatusMessage(null), 5000);
    return () => clearTimeout(timer);
  }, [workspaceStatusMessage]);

  async function refreshWorkspaceConfig() {
    try {
      const config = await apiRequest<WorkspaceConfig>("/api/workspace/config");
      setWorkspaceConfig(config);
      if (config.isAuthorized && config.rootPath) {
        void fetchWorkspaceFiles();
      }
    } catch {
      // Ignorato se il backend non è pronto
    }
  }

  async function fetchWorkspaceFiles() {
    try {
      const files = await apiRequest<WorkspaceFileItem[]>("/api/workspace/files");
      setWorkspaceFiles(files);
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'elenco dei file di progetto."
      );
    }
  }

  async function handlePickWindowsFolder() {
    setWorkspaceStatusMessage("Apertura dialogo sfoglia cartelle di Windows...");
    try {
      const config = await apiRequest<PickWorkspaceFolderResponse>("/api/workspace/pick-folder", {
        method: "POST"
      });
      if (config && !config.cancelled) {
        setWorkspaceConfig(config);
        setWorkspaceStatusMessage(
          `Cartella di progetto impostata: ${config.rootPath} (${config.fileCount} file)`
        );
        void fetchWorkspaceFiles();
      } else {
        setWorkspaceStatusMessage(null);
      }
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Impossibile aprire la selezione cartella."
      );
    }
  }

  async function handleAttachWorkspaceFile(relativePath: string) {
    try {
      const res = await apiRequest<ReadWorkspaceFileResponse>("/api/workspace/read-file", {
        method: "POST",
        body: JSON.stringify({ relativePath } as ReadWorkspaceFileRequest)
      });
      setSelectedWorkspaceFile(relativePath);
      setAttachedFileContent(res.content);
      setIsWorkspaceFilePickerOpen(false);
      setWorkspaceStatusMessage(`File allegato alla chat: ${relativePath}`);
    } catch (err) {
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore lettura file.");
    }
  }

  async function handleSaveAttachedFileContent(updatedContent: string, saveToDisk: boolean) {
    setAttachedFileContent(updatedContent);

    if (saveToDisk && selectedWorkspaceFile) {
      try {
        const res = await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
          method: "POST",
          body: JSON.stringify({
            relativePath: selectedWorkspaceFile,
            content: updatedContent
          } as WriteWorkspaceFileRequest)
        });
        setWorkspaceStatusMessage(res.message);
        void fetchWorkspaceFiles();
      } catch (err) {
        setWorkspaceStatusMessage(
          err instanceof Error ? err.message : "Errore durante il salvataggio su disco."
        );
      }
    } else {
      setWorkspaceStatusMessage(`Contesto allegato aggiornato per il file: ${selectedWorkspaceFile}`);
    }

    setIsAttachedFileEditorOpen(false);
  }

  async function handleOpenExternalFile(path: string) {
    try {
      await apiRequest<{ success: boolean }>("/api/workspace/open-external", {
        method: "POST",
        body: JSON.stringify({ path } as OpenExternalFileRequest)
      });
      setWorkspaceStatusMessage(`Apertura file nel programma locale: ${path}`);
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Impossibile aprire il file esternamente."
      );
    }
  }

  async function handleApplyCodeToFileSilently(relativePath: string, content: string) {
    try {
      await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
        method: "POST",
        body: JSON.stringify({ relativePath, content } as WriteWorkspaceFileRequest)
      });
    } catch {
      // Ignorato
    }
  }

  async function handleDeleteWorkspaceFileSilently(relativePath: string) {
    try {
      await apiRequest<DeleteWorkspaceFileResponse>("/api/workspace/delete-file", {
        method: "POST",
        body: JSON.stringify({ relativePath } as DeleteWorkspaceFileRequest)
      });
    } catch {
      // Ignorato
    }
  }

  function handleCancelGeneration() {
    abortControllerRef.current?.abort();
    setIsGenerating(false);
    setWorkspaceStatusMessage("Risposta interrotta dall'utente.");
  }

  function handleClearMessages() {
    if (isGenerating) {
      handleCancelGeneration();
    }
    setMessages([]);
    setError(null);
    setPromptInput("");
  }

  async function handleOpenDiff(relativePath: string, modifiedCode?: string, isApplied?: boolean) {
    try {
      let original = "";
      if (workspaceConfig?.isAuthorized) {
        const res = await apiRequest<ReadWorkspaceFileResponse>("/api/workspace/read-file", {
          method: "POST",
          body: JSON.stringify({ relativePath } as ReadWorkspaceFileRequest)
        }).catch(() => null);
        if (res) {
          original = res.content;
        }
      }
      setDiffModalFile(relativePath);
      setDiffModalOriginalContent(original);
      setDiffModalModifiedContent(modifiedCode ?? original);
      setDiffModalFileApplied(Boolean(isApplied));
    } catch {
      setDiffModalFile(relativePath);
      setDiffModalOriginalContent("");
      setDiffModalModifiedContent(modifiedCode ?? "");
      setDiffModalFileApplied(Boolean(isApplied));
    }
  }

  async function handleApplyCodeToFile(relativePath: string, content: string) {
    try {
      const res = await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
        method: "POST",
        body: JSON.stringify({ relativePath, content } as WriteWorkspaceFileRequest)
      });
      setWorkspaceStatusMessage(res.message);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante il salvataggio su disco."
      );
    }
  }

  async function handleRollbackFileContent(relativePath: string, originalContent: string) {
    if (!relativePath || !originalContent) return;
    try {
      const res = await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
        method: "POST",
        body: JSON.stringify({ relativePath, content: originalContent } as WriteWorkspaceFileRequest)
      });
      setWorkspaceStatusMessage(res.message || `⏪ Ripristinata la versione originale di ${relativePath}`);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante il ripristino del file."
      );
    }
  }

  async function handleDeleteWorkspaceFile(relativePath: string) {
    try {
      const res = await apiRequest<DeleteWorkspaceFileResponse>("/api/workspace/delete-file", {
        method: "POST",
        body: JSON.stringify({ relativePath } as DeleteWorkspaceFileRequest)
      });
      setWorkspaceStatusMessage(res.message);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'eliminazione del file."
      );
    }
  }

  async function handleExecuteWorkspaceCommand(cmdToRun?: string) {
    const cmd = cmdToRun || "dotnet build";
    setWorkspaceStatusMessage(`Esecuzione comando nel workspace: ${cmd}...`);
    try {
      const res = await apiRequest<ExecuteWorkspaceCommandResponse>(
        "/api/workspace/execute-command",
        {
          method: "POST",
          body: JSON.stringify({ command: cmd } as ExecuteWorkspaceCommandRequest)
        }
      );

      const logMsg = `💻 **Esecuzione Comando**: \`${cmd}\`\n\n${
        res.success
          ? "✅ Esecuzione completata con successo (Exit 0)"
          : `❌ Esecuzione terminata con errore (Exit ${res.exitCode})`
      }\n\n\`\`\`text\n${res.output || res.error || "Nessun output restituito dal processo."}\n\`\`\``;

      setMessages((prev) => [
        ...prev,
        {
          id: `cmd_${Date.now()}`,
          sender: "assistant",
          content: logMsg,
          timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
        }
      ]);
      setWorkspaceStatusMessage(
        res.success
          ? `Comando eseguito con successo (${cmd})`
          : `Comando completato con errore (${cmd})`
      );
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'esecuzione del comando."
      );
    }
  }



  function extractFileActionsFromResponse(text: string): FileAction[] {
    const actions: FileAction[] = [];

    const fileRegex =
      /(?:Target File|File|File Modificato|Salva in|Modificato|Nel file|Codice per|Refattorizzato):\s*`?([a-zA-Z0-9_\-./\\]+\.[a-zA-Z0-9]+)`?/gi;
    let match;
    while ((match = fileRegex.exec(text)) !== null) {
      const filePath = match[1];
      if (filePath && !actions.some((a) => a.file === filePath)) {
        const afterText = text.slice(match.index);
        const codeBlockMatch = /```(?:\w+)?\r?\n(.*?)\r?\n```/s.exec(afterText);
        actions.push({
          file: filePath,
          action: "write",
          code: codeBlockMatch ? codeBlockMatch[1].trim() : undefined
        });
      }
    }

    const headerRegex =
      /(?:###|\*\*|`)\s*([a-zA-Z0-9_\-./\\]+\.(?:cs|ts|tsx|js|jsx|json|xml|csproj|sln|md|txt))\b/gi;
    let headerMatch;
    while ((headerMatch = headerRegex.exec(text)) !== null) {
      const filePath = headerMatch[1];
      if (filePath && !actions.some((a) => a.file === filePath)) {
        const afterText = text.slice(headerMatch.index);
        const codeBlockMatch = /```(?:\w+)?\r?\n(.*?)\r?\n```/s.exec(afterText);
        if (codeBlockMatch) {
          actions.push({
            file: filePath,
            action: "write",
            code: codeBlockMatch[1].trim()
          });
        }
      }
    }

    if (workspaceFiles && workspaceFiles.length > 0) {
      for (const wf of workspaceFiles) {
        if (!wf.isDirectory && wf.relativePath) {
          const fileName = wf.relativePath.split("/").pop() || wf.relativePath;
          if (!actions.some((a) => a.file === wf.relativePath || a.file === fileName)) {
            const idx = text.indexOf(fileName);
            if (idx !== -1) {
              const afterText = text.slice(idx);
              const codeBlockMatch = /```(?:\w+)?\r?\n(.*?)\r?\n```/s.exec(afterText);
              if (codeBlockMatch) {
                actions.push({
                  file: wf.relativePath,
                  action: "write",
                  code: codeBlockMatch[1].trim()
                });
              }
            }
          }
        }
      }
    }

    const deleteRegex = /ACTION:\s*DELETE\s+`?([a-zA-Z0-9_\-./\\]+\.[a-zA-Z0-9]+)`?/gi;
    let delMatch;
    while ((delMatch = deleteRegex.exec(text)) !== null) {
      const filePath = delMatch[1];
      if (filePath && !actions.some((a) => a.file === filePath)) {
        actions.push({
          file: filePath,
          action: "delete"
        });
      }
    }

    return actions;
  }

  async function handleApproveAgentToolCall(callId: string, approved: boolean) {
    try {
      await apiRequest<{ success: boolean }>("/api/agent/approve-tool", {
        method: "POST",
        body: JSON.stringify({ callId, approved } as ApproveToolCallRequest)
      });
    } catch {
      // Ignorato
    }
  }

  async function handleSendAgentMessage(textToSend: string) {
    const userMessageId = `user_${Date.now()}`;
    const userMsg: CodingMessage = {
      id: userMessageId,
      sender: "user",
      content: textToSend,
      timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
      attachedFile: selectedWorkspaceFile ?? undefined
    };

    const assistantMessageId = `agent_${Date.now()}`;
    const assistantMsg: CodingMessage = {
      id: assistantMessageId,
      sender: "assistant",
      content: "",
      timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
      isStreaming: true,
      agentEvents: []
    };

    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setPromptInput("");
    setIsGenerating(true);
    setError(null);

    try {
      await apiAgentStreamRequest(
        "/api/agent/run-stream",
        {
          goal: textToSend,
          model: selectedModel || null,
          mode: operatingMode,
          workspaceRoot: workspaceConfig?.rootPath || null,
          autoApproveCommands
        } as AgentRunRequest,
        (rawEvent) => {
          const event = rawEvent as AgentStepEvent;
          setMessages((prev) =>
            prev.map((msg) => {
              if (msg.id !== assistantMessageId) return msg;

              const existingEvents = msg.agentEvents ? [...msg.agentEvents] : [];

              if (event.type === "thought_chunk" && event.content) {
                const lastIdx = existingEvents.length - 1;
                if (lastIdx >= 0 && existingEvents[lastIdx].type === "thought_chunk") {
                  existingEvents[lastIdx] = {
                    ...existingEvents[lastIdx],
                    content: (existingEvents[lastIdx].content || "") + event.content
                  };
                } else {
                  existingEvents.push(event);
                }
                return { ...msg, agentEvents: existingEvents };
              }

              const updatedEvents = [...existingEvents, event];
              const currentActions = msg.fileActions ? [...msg.fileActions] : [];
              if (event.type === "tool_result" && event.toolResult && event.toolResult.success) {
                const tr = event.toolResult;
                const toolName = tr.toolName.toLowerCase();
                if (
                  toolName === "write_file" ||
                  toolName === "write_to_file" ||
                  toolName === "replace_file_content"
                ) {
                  const matchingProp = existingEvents.find(
                    (e) =>
                      e.type === "tool_proposed" && e.toolCall && e.toolCall.callId === tr.callId
                  );
                  if (matchingProp?.toolCall) {
                    try {
                      const args = JSON.parse(
                        matchingProp.toolCall.argumentsJson
                      ) as Record<string, string>;
                      const filePath = args.relativePath || args.path || args.file;
                      if (filePath && !currentActions.some((a) => a.file === filePath)) {
                        currentActions.push({
                          file: filePath,
                          action: "write",
                          code: args.content || args.replacementContent,
                          applied: true
                        });
                      }
                    } catch {
                      // Ignora se i parametri non sono JSON valido
                    }
                  }
                  void fetchWorkspaceFiles();
                }
              }

              const finalContent =
                event.type === "final_response" && event.content
                  ? event.content
                  : msg.content;

              const modified = currentActions.map((a) => a.file);

              return {
                ...msg,
                agentEvents: updatedEvents,
                content: finalContent,
                fileActions: currentActions.length > 0 ? currentActions : undefined,
                modifiedFiles: modified.length > 0 ? modified : undefined
              };
            })
          );
        },
        abortController.signal
      );

      setMessages((prev) =>
        prev.map((msg) => {
          if (msg.id !== assistantMessageId) return msg;

          const actions = msg.fileActions ? [...msg.fileActions] : [];
          if (msg.content) {
            const extracted = extractFileActionsFromResponse(msg.content);
            for (const ext of extracted) {
              if (!actions.some((a) => a.file === ext.file)) {
                actions.push({ ...ext, applied: true });
              }
            }
          }

          if (operatingMode === "write" && actions.length > 0 && workspaceConfig?.isAuthorized) {
            for (const act of actions) {
              if (!act.applied && act.action === "write" && act.code) {
                void handleApplyCodeToFileSilently(act.file, act.code);
                act.applied = true;
              } else if (!act.applied && act.action === "delete") {
                void handleDeleteWorkspaceFileSilently(act.file);
                act.applied = true;
              }
            }
          }

          const modified = actions.map((a) => a.file);
          return {
            ...msg,
            isStreaming: false,
            fileActions: actions.length > 0 ? actions : undefined,
            modifiedFiles: modified.length > 0 ? modified : undefined
          };
        })
      );
      void fetchWorkspaceFiles();
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        setWorkspaceStatusMessage("Risposta agente interrotta.");
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === assistantMessageId ? { ...msg, isStreaming: false } : msg
          )
        );
        return;
      }
      const errMsg = err instanceof Error ? err.message : "Errore durante l'esecuzione dell'agente.";
      setError(errMsg);
      setMessages((prev) =>
        prev.map((msg) =>
          msg.id === assistantMessageId
            ? { ...msg, content: `⚠️ Errore: ${errMsg}`, isStreaming: false }
            : msg
        )
      );
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleSendMessage(overridePrompt?: string) {
    const textToSend = overridePrompt ?? promptInput;
    if (!textToSend.trim() || isGenerating) return;

    if (!workspaceConfig?.isAuthorized) {
      setWorkspaceStatusMessage(
        "⚠️ Selezionare una cartella di progetto prima di avviare l'Agente Autonomo."
      );
      void handlePickWindowsFolder();
      return;
    }
    return handleSendAgentMessage(textToSend);
  }

  return {
    selectedModel,
    setSelectedModel,
    operatingMode,
    setOperatingMode,
    autoApproveCommands,
    setAutoApproveCommands,
    promptInput,
    setPromptInput,
    messages,
    isGenerating,
    error,
    workspaceConfig,
    workspaceFiles,
    selectedWorkspaceFile,
    setSelectedWorkspaceFile,
    attachedFileContent,
    setAttachedFileContent,
    isWorkspaceFilePickerOpen,
    setIsWorkspaceFilePickerOpen,
    isAttachedFileEditorOpen,
    setIsAttachedFileEditorOpen,
    workspaceStatusMessage,
    diffModalFile,
    setDiffModalFile,
    diffModalOriginalContent,
    diffModalModifiedContent,
    diffModalFileApplied,
    chatContainerRef,
    handlePickWindowsFolder,
    handleAttachWorkspaceFile,
    handleSaveAttachedFileContent,
    handleOpenExternalFile,
    handleCancelGeneration,
    handleClearMessages,
    handleOpenDiff,
    handleApplyCodeToFile,
    handleRollbackFileContent,
    handleDeleteWorkspaceFile,
    handleApproveAgentToolCall,
    handleExecuteWorkspaceCommand,
    handleSendMessage
  };
}
