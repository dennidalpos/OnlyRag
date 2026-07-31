import { useRef, useState } from "react";
import {
  apiAgentStreamRequest,
  apiRequest,
  type AgentRunRequest,
  type AgentStepEvent,
  type ApproveToolCallRequest
} from "../api";
import type { WorkspaceFileItem } from "../apiTypes";
import type { CodingMessage } from "./useCodingSectionController";
import type { FileAction } from "./CodingSection.types";

export type UseAgentStreamHandlerOptions = {
  selectedModel: string;
  operatingMode: "plan" | "write";
  autoApproveCommands: boolean;
  workspaceConfig: { isAuthorized: boolean; rootPath: string | null } | null;
  workspaceFiles: WorkspaceFileItem[];
  selectedWorkspaceFile: string | null;
  fetchWorkspaceFiles: () => Promise<void>;
  handleApplyCodeToFileSilently: (relativePath: string, content: string) => Promise<void>;
  handleDeleteWorkspaceFileSilently: (relativePath: string) => Promise<void>;
  setWorkspaceStatusMessage: (msg: string | null) => void;
};

export function useAgentStreamHandler({
  selectedModel,
  operatingMode,
  autoApproveCommands,
  workspaceConfig,
  workspaceFiles,
  selectedWorkspaceFile,
  fetchWorkspaceFiles,
  handleApplyCodeToFileSilently,
  handleDeleteWorkspaceFileSilently,
  setWorkspaceStatusMessage
}: UseAgentStreamHandlerOptions) {
  const [messages, setMessages] = useState<CodingMessage[]>([]);
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [promptInput, setPromptInput] = useState("");

  const chatContainerRef = useRef<HTMLDivElement | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

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

  return {
    messages,
    setMessages,
    isGenerating,
    error,
    promptInput,
    setPromptInput,
    chatContainerRef,
    handleCancelGeneration,
    handleClearMessages,
    handleApproveAgentToolCall,
    handleSendAgentMessage
  };
}
