import { useCallback, useEffect, useRef, useState } from "react";
import {
  apiAgentStreamRequest,
  apiRequest,
  type AgentRunRequest,
  type AgentRunSnapshot,
  type AgentStepEvent,
  type ApproveToolCallRequest
} from "../../api";
import type { CodingMessage } from "./useCodingSectionController";
import type { CodingMode } from "./CodingSection.types";

export type UseAgentStreamHandlerOptions = {
  selectedModel: string;
  operatingMode: CodingMode;
  autoApproveCommands: boolean;
  workspaceConfig: { isAuthorized: boolean; rootPath: string | null } | null;
  fetchWorkspaceFiles: () => Promise<void>;
  setWorkspaceStatusMessage: (msg: string | null) => void;
};

function toAgentMode(mode: CodingMode): AgentRunRequest["mode"] {
  return mode === "full" ? "write" : mode;
}

export function useAgentStreamHandler({
  selectedModel,
  operatingMode,
  autoApproveCommands,
  workspaceConfig,
  fetchWorkspaceFiles,
  setWorkspaceStatusMessage
}: UseAgentStreamHandlerOptions) {
  const [messages, setMessages] = useState<CodingMessage[]>([]);
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [promptInput, setPromptInputState] = useState("");
  const [resumableRuns, setResumableRuns] = useState<AgentRunSnapshot[]>([]);
  const promptInputRef = useRef("");
  const chatContainerRef = useRef<HTMLDivElement | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  const refreshResumableRuns = useCallback(async () => {
    try {
      const runs = await apiRequest<AgentRunSnapshot[]>("/api/agent/runs/resumable");
      setResumableRuns(runs);
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Impossibile caricare le sessioni riprendibili.");
    }
  }, []);

  useEffect(() => {
    void refreshResumableRuns();
  }, [refreshResumableRuns]);

  function setPromptInput(value: string) {
    promptInputRef.current = value;
    setPromptInputState(value);
  }

  function handleCancelGeneration() {
    abortControllerRef.current?.abort();
    setWorkspaceStatusMessage("Esecuzione interrotta dall'utente.");
  }

  function handleClearMessages() {
    if (isGenerating) {
      handleCancelGeneration();
    }
    setMessages([]);
    setError(null);
    setPromptInput("");
  }

  const [activeRunId, setActiveRunId] = useState<string | null>(null);

  async function handleApproveAgentToolCall(callId: string, approved: boolean) {
    try {
      const response = await apiRequest<{ success: boolean }>("/api/agent/approve-tool", {
        method: "POST",
        body: JSON.stringify({ callId, approved } satisfies ApproveToolCallRequest)
      });
      if (!response.success) {
        throw new Error("La richiesta di approvazione non e piu attiva.");
      }

      if (approved) {
        const targetRun = resumableRuns.find((r) => r.runId === activeRunId) ?? (activeRunId ? { runId: activeRunId, goal: "Esegui il piano", mode: "write", model: selectedModel, workspaceRoot: workspaceConfig?.rootPath || null, phase: "Act", toolCallsUsed: 0, estimatedTokensUsed: 0, startedAtUtc: "", updatedAtUtc: "" } as AgentRunSnapshot : undefined);
        void handleSendAgentMessage("Piano approvato dall'utente. Esegui le modifiche e verifica.", targetRun);
      }
    } catch (requestError) {
      setError(requestError instanceof Error ? requestError.message : "Impossibile registrare l'approvazione.");
    }
  }

  async function handleSendAgentMessage(textToSend: string, resumedRun?: AgentRunSnapshot) {
    const targetRunId = resumedRun?.runId ?? activeRunId ?? (resumableRuns.length > 0 ? resumableRuns[0].runId : null);
    const goal = resumedRun?.goal ?? textToSend;
    const resumedMode = resumedRun?.mode?.toLowerCase();
    const mode = resumedMode === "write" || resumedMode === "full"
      ? "write"
      : resumedMode === "ask"
        ? "ask"
        : resumedRun
          ? "plan"
          : toAgentMode(operatingMode);
    const userMessageId = `user_${Date.now()}`;
    const assistantMessageId = `agent_${Date.now()}`;
    const timestamp = new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    const abortController = new AbortController();

    abortControllerRef.current = abortController;
    setMessages((current) => [
      ...current,
      {
        id: userMessageId,
        sender: "user",
        content: resumedRun ? `Riprendi: ${goal}` : textToSend,
        timestamp
      },
      {
        id: assistantMessageId,
        sender: "assistant",
        content: "",
        timestamp,
        isStreaming: true,
        agentEvents: []
      }
    ]);
    setPromptInput("");
    setIsGenerating(true);
    setError(null);

    try {
      await apiAgentStreamRequest(
        "/api/agent/run-stream",
        {
          goal,
          model: resumedRun?.model ?? (selectedModel || null),
          mode,
          workspaceRoot: resumedRun?.workspaceRoot || workspaceConfig?.rootPath || null,
          autoApproveCommands,
          resumeRunId: targetRunId
        } satisfies AgentRunRequest,
        (rawEvent: unknown) => {
          const event = rawEvent as AgentStepEvent;
          if (event.runId) {
            setActiveRunId(event.runId);
          }
          setMessages((current) =>
            current.map((message) => {
              if (message.id !== assistantMessageId) return message;
              const events = [...(message.agentEvents ?? []), event];
              return {
                ...message,
                agentEvents: events,
                runId: event.runId ?? message.runId,
                content: event.type === "final_response" && event.content ? event.content : message.content
              };
            })
          );
        },
        abortController.signal
      );
      await fetchWorkspaceFiles();
    } catch (requestError) {
      if (requestError instanceof Error && requestError.name === "AbortError") {
        setMessages((current) =>
          current.map((message) => message.id === assistantMessageId ? { ...message, isStreaming: false } : message)
        );
        return;
      }

      const message = requestError instanceof Error ? requestError.message : "Errore durante l'esecuzione dell'agente.";
      setError(message);
      setMessages((current) =>
        current.map((item) =>
          item.id === assistantMessageId
            ? { ...item, agentEvents: [...(item.agentEvents ?? []), { type: "error", content: message }], isStreaming: false }
            : item
        )
      );
    } finally {
      setMessages((current) =>
        current.map((message) => message.id === assistantMessageId ? { ...message, isStreaming: false } : message)
      );
      setIsGenerating(false);
      void refreshResumableRuns();
    }
  }

  return {
    messages,
    isGenerating,
    error,
    promptInput,
    promptInputRef,
    resumableRuns,
    setPromptInput,
    chatContainerRef,
    handleCancelGeneration,
    handleClearMessages,
    handleApproveAgentToolCall,
    handleSendAgentMessage,
    refreshResumableRuns
  };
}
