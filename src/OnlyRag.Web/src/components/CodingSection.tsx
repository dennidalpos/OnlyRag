import { useEffect, useRef, useState } from "react";
import {
  apiAgentStreamRequest,
  apiRequest,
  apiStreamRequest,
  type AgentRunRequest,
  type AgentStepEvent,
  type ApproveToolCallRequest,
  type OllamaModel
} from "../api";
import type {
  CodingTaskRequest,
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
import { AgentToolCallCard } from "./AgentToolCallCard";
import { formatWorkspaceTreeSummary } from "./CodingSection.helpers";
import {
  deleteCustomPreset,
  loadSavedPresets,
  saveCustomPreset,
  type VibePreset
} from "./CodingSection.storage";
import { AttachedFileEditorModal, DiffViewerModal, WorkspaceFilePickerModal } from "./CodingSection.views";

type FileAction = {
  file: string;
  action: "write" | "delete";
  code?: string;
  applied?: boolean;
};

type CodingMessage = {
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
  const [selectedModel, setSelectedModel] = useState<string>("");
  const [operatingMode, setOperatingMode] = useState<"plan" | "write">("write");
  const [useAgentMode, setUseAgentMode] = useState(true);
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

  // Vibe Presets state
  const [presets, setPresets] = useState<VibePreset[]>([]);
  const [showAddPresetModal, setShowAddPresetModal] = useState(false);
  const [newPresetTitle, setNewPresetTitle] = useState("");
  const [newPresetIcon, setNewPresetIcon] = useState("CODE");
  const [newPresetPrompt, setNewPresetPrompt] = useState("");

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
    setPresets(loadSavedPresets());
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
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore durante l'elenco dei file di progetto.");
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
        setWorkspaceStatusMessage(`Cartella di progetto impostata: ${config.rootPath} (${config.fileCount} file)`);
        void fetchWorkspaceFiles();
      } else {
        setWorkspaceStatusMessage(null);
      }
    } catch (err) {
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Impossibile aprire la selezione cartella.");
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
          body: JSON.stringify({ relativePath: selectedWorkspaceFile, content: updatedContent } as WriteWorkspaceFileRequest)
        });
        setWorkspaceStatusMessage(res.message);
        void fetchWorkspaceFiles();
      } catch (err) {
        setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore durante il salvataggio su disco.");
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
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Impossibile aprire il file esternamente.");
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

  async function handleOpenDiff(relativePath: string, modifiedCode?: string) {
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
    } catch {
      setDiffModalFile(relativePath);
      setDiffModalOriginalContent("");
      setDiffModalModifiedContent(modifiedCode ?? "");
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
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore durante il salvataggio su disco.");
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
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore durante l'eliminazione del file.");
    }
  }

  async function handleExecuteWorkspaceCommand(cmdToRun?: string) {
    const cmd = cmdToRun || "dotnet build";
    setWorkspaceStatusMessage(`Esecuzione comando nel workspace: ${cmd}...`);
    try {
      const res = await apiRequest<ExecuteWorkspaceCommandResponse>("/api/workspace/execute-command", {
        method: "POST",
        body: JSON.stringify({ command: cmd } as ExecuteWorkspaceCommandRequest)
      });

      const logMsg = `💻 **Esecuzione Comando**: \`${cmd}\`\n\n${res.success ? "✅ Esecuzione completata con successo (Exit 0)" : `❌ Esecuzione terminata con errore (Exit ${res.exitCode})`}\n\n\`\`\`text\n${res.output || res.error || "Nessun output restituito dal processo."}\n\`\`\``;

      setMessages((prev) => [
        ...prev,
        {
          id: `cmd_${Date.now()}`,
          sender: "assistant",
          content: logMsg,
          timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
        }
      ]);
      setWorkspaceStatusMessage(res.success ? `Comando eseguito con successo (${cmd})` : `Comando completato con errore (${cmd})`);
    } catch (err) {
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore durante l'esecuzione del comando.");
    }
  }

  function handleAddPreset() {
    if (!newPresetTitle.trim() || !newPresetPrompt.trim()) return;
    const updated = saveCustomPreset({
      title: newPresetTitle.trim(),
      icon: newPresetIcon.trim() || "⚡",
      prompt: newPresetPrompt.trim()
    });
    setPresets(updated);
    setShowAddPresetModal(false);
    setNewPresetTitle("");
    setNewPresetIcon("⚡");
    setNewPresetPrompt("");
  }

  function handleDeletePreset(id: string) {
    const updated = deleteCustomPreset(id);
    setPresets(updated);
  }

  function applyPreset(preset: VibePreset) {
    if (promptInput.trim()) {
      setPromptInput((prev) => `${prev}\n\n${preset.prompt}`);
    } else {
      setPromptInput(preset.prompt);
    }
  }

  function extractFileActionsFromResponse(text: string): FileAction[] {
    const actions: FileAction[] = [];

    const fileRegex = /(?:Target File|File|File Modificato|Salva in|Modificato):\s*`?([a-zA-Z0-9_\-./\\]+\.[a-zA-Z0-9]+)`?/gi;
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
              const finalContent = event.type === "final_response" && event.content
                ? event.content
                : msg.content;
              return { ...msg, agentEvents: updatedEvents, content: finalContent };
            })
          );
        },
        abortController.signal
      );

      setMessages((prev) =>
        prev.map((msg) =>
          msg.id === assistantMessageId ? { ...msg, isStreaming: false } : msg
        )
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

    if (useAgentMode) {
      if (!workspaceConfig?.isAuthorized) {
        setWorkspaceStatusMessage("⚠️ Selezionare una cartella di progetto prima di avviare l'Agente Autonomo.");
        void handlePickWindowsFolder();
        return;
      }
      return handleSendAgentMessage(textToSend);
    }

    const userMessageId = `user_${Date.now()}`;
    const userMsg: CodingMessage = {
      id: userMessageId,
      sender: "user",
      content: textToSend,
      timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
      attachedFile: selectedWorkspaceFile ?? undefined
    };

    const assistantMessageId = `assistant_${Date.now()}`;
    const assistantMsg: CodingMessage = {
      id: assistantMessageId,
      sender: "assistant",
      content: "",
      timestamp: new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }),
      isStreaming: true
    };

    const abortController = new AbortController();
    abortControllerRef.current = abortController;

    setMessages((prev) => [...prev, userMsg, assistantMsg]);
    setPromptInput("");
    setIsGenerating(true);
    setError(null);

    let fullResponseText = "";
    const workspaceSummary = workspaceConfig?.isAuthorized
      ? formatWorkspaceTreeSummary(workspaceConfig.rootPath, workspaceFiles)
      : null;

    try {
      await apiStreamRequest(
        "/api/coding/generate-stream",
        {
          prompt: textToSend,
          model: selectedModel || null,
          codeContext: attachedFileContent || null,
          targetFilePath: selectedWorkspaceFile || null,
          mode: operatingMode,
          workspaceSummary
        } as CodingTaskRequest,
        (chunk) => {
          fullResponseText += chunk;
          setMessages((prev) =>
            prev.map((msg) =>
              msg.id === assistantMessageId
                ? { ...msg, content: fullResponseText }
                : msg
            )
          );
        },
        abortController.signal
      );

      const actions = extractFileActionsFromResponse(fullResponseText);
      const modified = actions.map((a) => a.file);

      // In Write mode, automatically apply modifications / deletions to workspace files!
      if (operatingMode === "write" && actions.length > 0 && workspaceConfig?.isAuthorized) {
        for (const act of actions) {
          if (act.action === "write" && act.code) {
            await handleApplyCodeToFileSilently(act.file, act.code);
            act.applied = true;
          } else if (act.action === "delete") {
            await handleDeleteWorkspaceFileSilently(act.file);
            act.applied = true;
          }
        }
        void fetchWorkspaceFiles();
        setWorkspaceStatusMessage(`Modifiche applicate con successo al workspace (${actions.length} file).`);
      }

      setMessages((prev) =>
        prev.map((msg) =>
          msg.id === assistantMessageId
            ? {
                ...msg,
                isStreaming: false,
                modifiedFiles: modified.length > 0 ? modified : undefined,
                fileActions: actions.length > 0 ? actions : undefined
              }
            : msg
        )
      );

      // If response includes COMMAND: or prompt asks to build/run
      const commandMatch = /COMMAND:\s*`?([^`\r\n]+)`?/i.exec(fullResponseText);
      const lowerPrompt = textToSend.toLowerCase();
      if (commandMatch && commandMatch[1]) {
        void handleExecuteWorkspaceCommand(commandMatch[1].trim());
      } else if (lowerPrompt.includes("compila") || lowerPrompt.includes("avvia") || lowerPrompt.includes("build") || lowerPrompt.includes("run")) {
        void handleExecuteWorkspaceCommand("dotnet build");
      }
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        setWorkspaceStatusMessage("Risposta interrotta.");
        setMessages((prev) =>
          prev.map((msg) =>
            msg.id === assistantMessageId ? { ...msg, isStreaming: false } : msg
          )
        );
        return;
      }
      const errMsg = err instanceof Error ? err.message : "Errore durante la generazione.";
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

  return (
    <section className="coding-section" aria-label="Coding e Vibe Hub" style={{ width: "100%", display: "flex", flexDirection: "column" }}>
      {/* HEADER SECTION */}
      <header className="coding-section__header" style={{ marginBottom: 12, display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <div className="coding-section__title-group">
          <h2 style={{ display: "flex", alignItems: "center", gap: 8, margin: 0 }}>
            <span>⚡</span> Coding & Vibe Hub
          </h2>
          <p className="coding-section__subtitle" style={{ margin: "4px 0 0 0", color: "#94a3b8", fontSize: "0.85rem" }}>
            Assistente per analisi, scrittura codice, gestione file e compilazione di progetto.
          </p>
        </div>

        <div className="coding-section__controls" style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <div className="model-selector-group" style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <label htmlFor="coding-model-select" className="field-label-inline" style={{ fontSize: "0.85rem", color: "#cbd5e1" }}>
              Modello:
            </label>
            <select
              id="coding-model-select"
              className="input-control select-control--inline"
              style={{ background: "#0f172a", border: "1px solid #334155", color: "#f8fafc", padding: "4px 8px", borderRadius: 6 }}
              value={selectedModel}
              onChange={(e) => setSelectedModel(e.target.value)}
            >
              {models.map((m) => (
                <option key={m.name} value={m.name}>
                  {m.name} {m.name === defaultModel ? "(Predefinito)" : ""}
                </option>
              ))}
              {models.length === 0 && <option value="">Nessun modello Ollama rilevato</option>}
            </select>
          </div>
        </div>
      </header>

      {/* WORKSPACE FOLDER BANNER */}
      <div
        className="workspace-banner"
        style={{
          background: "linear-gradient(135deg, #1e293b 0%, #0f172a 100%)",
          padding: "12px 18px",
          borderRadius: 10,
          marginBottom: 14,
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          border: "1px solid #334155",
          boxShadow: "0 4px 12px rgba(0,0,0,0.15)",
          width: "100%"
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 14 }}>
          <span style={{ fontSize: "1.5rem" }}>📂</span>
          <div>
            <strong style={{ color: "#f8fafc", fontSize: "0.95rem" }}>
              {workspaceConfig?.isAuthorized
                ? `Cartella Progetto: ${workspaceConfig.rootPath}`
                : "Nessuna cartella di progetto impostata"}
            </strong>
            <div style={{ fontSize: "0.82rem", color: "#94a3b8", marginTop: 2 }}>
              {workspaceConfig?.isAuthorized
                ? `${workspaceConfig.fileCount} file indicizzati nel workspace`
                : "Seleziona una cartella per abilitare l'analisi dei file e i comandi di build."}
            </div>
          </div>
        </div>

        <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
          {workspaceConfig?.isAuthorized && (
            <button
              type="button"
              className="button button--secondary button--small"
              onClick={() => setIsWorkspaceFilePickerOpen(true)}
            >
              📄 File ({workspaceFiles.length})
            </button>
          )}
          <button
            type="button"
            className="button button--primary button--small"
            style={{ background: "#2563eb", borderColor: "#3b82f6" }}
            onClick={() => void handlePickWindowsFolder()}
          >
            📂 Sfoglia Cartella
          </button>
        </div>
      </div>


      {workspaceStatusMessage && (
        <div className="feedback-banner feedback-banner--info" style={{ marginBottom: 14 }}>
          {workspaceStatusMessage}
        </div>
      )}

      {/* CODE AUDIT & VIBE PRESETS BAR */}
      <div className="vibe-presets-bar" style={{ marginBottom: 14, width: "100%" }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 8 }}>
          <span style={{ fontSize: "0.85rem", fontWeight: 600, color: "#cbd5e1" }}>
            ⚡ Preset Vibe Coding & Audit:
          </span>
          <button
            type="button"
            className="button button--secondary button--small"
            style={{ fontSize: "0.78rem" }}
            onClick={() => setShowAddPresetModal(true)}
          >
            ➕ Nuovo Preset
          </button>
        </div>

        <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
          {presets.map((preset) => (
            <div key={preset.id} style={{ display: "inline-flex", alignItems: "center" }}>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{
                  display: "inline-flex",
                  alignItems: "center",
                  gap: 6,
                  background: preset.id.startsWith("preset_audit") ? "#1e1b4b" : "#1e293b",
                  borderColor: preset.id.startsWith("preset_audit") ? "#6366f1" : "#475569",
                  color: preset.id.startsWith("preset_audit") ? "#c7d2fe" : "#f1f5f9",
                  borderRadius: preset.isBuiltIn ? 6 : "6px 0 0 6px",
                  fontWeight: 600
                }}
                onClick={() => applyPreset(preset)}
              >
                <span>{preset.icon}</span>
                <span>{preset.title}</span>
              </button>
              {!preset.isBuiltIn && (
                <button
                  type="button"
                  style={{
                    background: "#7f1d1d",
                    border: "1px solid #475569",
                    borderLeft: "none",
                    color: "#fca5a5",
                    padding: "4px 6px",
                    borderRadius: "0 6px 6px 0",
                    cursor: "pointer",
                    fontSize: "0.75rem"
                  }}
                  onClick={() => handleDeletePreset(preset.id)}
                  title="Elimina preset personalizzato"
                >
                  ✕
                </button>
              )}
            </div>
          ))}
        </div>
      </div>

      {loadError && (
        <div className="feedback-banner feedback-banner--warning" role="alert">
          {loadError}
        </div>
      )}

      {error && (
        <div className="feedback-banner feedback-banner--error" role="alert">
          {error}
        </div>
      )}

      {/* MAIN CHAT CONTAINER */}
      <div
        ref={chatContainerRef}
        className="vibe-chat-messages"
        style={{
          width: "100%",
          flex: 1,
          minHeight: 360,
          maxHeight: 520,
          overflowY: "auto",
          background: "#060911",
          borderRadius: 10,
          border: "1px solid #334155",
          padding: 16,
          marginBottom: 14,
          display: "flex",
          flexDirection: "column",
          gap: 16
        }}
      >
        {messages.length === 0 ? (
          <div style={{ margin: "auto", textAlign: "center", color: "#64748b", maxWidth: 500 }}>
            <div style={{ fontSize: "2.8rem", marginBottom: 8 }}>{useAgentMode ? "🤖" : "🚀"}</div>
            <h3 style={{ color: "#e2e8f0", marginBottom: 6 }}>{useAgentMode ? "Agente Autonomo" : "Coding & Vibe Hub"}</h3>
            <p style={{ fontSize: "0.9rem", lineHeight: 1.5, color: "#94a3b8" }}>
              {useAgentMode
                ? "Modalità Agente attiva. Scrivi un obiettivo in linguaggio naturale e l'agente esplorerà il progetto, leggerà i file, applicherà le modifiche ed eseguirà i comandi necessari."
                : "Seleziona la cartella del progetto per consentire all'assistente di analizzare i file, applicare modifiche ed eseguire comandi."}
            </p>
          </div>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              style={{
                alignSelf: msg.sender === "user" ? "flex-end" : "flex-start",
                maxWidth: "92%",
                width: msg.agentEvents && msg.agentEvents.length > 0 ? "100%" : "fit-content",
                background: msg.sender === "user" ? "#1e3a8a" : "#1e293b",
                color: "#f8fafc",
                borderRadius: 10,
                padding: "12px 16px",
                border: msg.sender === "user" ? "1px solid #2563eb" : "1px solid #334155",
                boxShadow: "0 2px 8px rgba(0,0,0,0.2)"
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12, marginBottom: 6, fontSize: "0.78rem", color: "#94a3b8" }}>
                <strong>{msg.sender === "user" ? "👤 Tu" : (msg.agentEvents ? `🤖 Agente (${selectedModel})` : `🤖 Assistant (${selectedModel})`)}</strong>
                <span>{msg.timestamp}</span>
              </div>

              {msg.attachedFile && (
                <div style={{ background: "#0f172a", padding: "4px 8px", borderRadius: 4, fontSize: "0.8rem", color: "#38bdf8", marginBottom: 8 }}>
                  📄 File allegato: {msg.attachedFile}
                </div>
              )}

              {/* AGENT EVENTS RENDERING */}
              {(useAgentMode && msg.sender === "assistant") || (msg.agentEvents && msg.agentEvents.length > 0) ? (
                <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
                  {msg.agentEvents?.map((evt, idx) => (
                    <AgentToolCallCard
                      key={`${msg.id}_evt_${idx}`}
                      event={evt}
                      onApprove={(callId, approved) => void handleApproveAgentToolCall(callId, approved)}
                    />
                  ))}
                  {msg.isStreaming && (() => {
                    const lastEvt = msg.agentEvents && msg.agentEvents.length > 0 ? msg.agentEvents[msg.agentEvents.length - 1] : null;
                    let statusText = "Agente in esecuzione: caricamento modello LLM ed elaborazione in corso...";
                    if (lastEvt) {
                      if (lastEvt.type === "tool_proposed" && lastEvt.toolCall) {
                        statusText = `🛠️ Richiesta tool '${lastEvt.toolCall.toolName}' in corso...`;
                      } else if (lastEvt.type === "tool_result" && lastEvt.toolResult) {
                        statusText = `✅ Completato tool '${lastEvt.toolResult.toolName}'. Analisi dei risultati...`;
                      } else if (lastEvt.type === "thought" || lastEvt.type === "thought_chunk") {
                        const snippet = (lastEvt.content || "").trim().slice(-120);
                        statusText = snippet ? `⚡ ${snippet}` : "⚡ Pensiero ed elaborazione LLM in corso...";
                      } else if (lastEvt.type === "approval_required" && lastEvt.toolCall) {
                        statusText = `⚠️ In attesa di approvazione utente per '${lastEvt.toolCall.toolName}'...`;
                      }
                    }
                    return (
                      <div style={{
                        display: "flex",
                        alignItems: "center",
                        gap: 10,
                        background: "linear-gradient(135deg, #1e1b4b 0%, #0f172a 100%)",
                        border: "1px solid #6366f1",
                        color: "#c7d2fe",
                        padding: "8px 12px",
                        borderRadius: 8,
                        fontSize: "0.84rem",
                        fontWeight: 600,
                        marginTop: 4,
                        boxShadow: "0 0 12px rgba(99,102,241,0.2)"
                      }}>
                        <span style={{ fontSize: "1.1rem", animation: "spin 1.5s linear infinite" }}>⏳</span>
                        <span style={{ whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{statusText}</span>
                      </div>
                    );
                  })()}
                </div>
              ) : (
                <div style={{ whiteSpace: "pre-wrap", fontFamily: msg.sender === "assistant" ? "monospace" : "inherit", fontSize: "0.9rem", lineHeight: 1.6 }}>
                  {msg.content}
                  {msg.isStreaming && (
                    <div style={{
                      display: "inline-flex",
                      alignItems: "center",
                      gap: 8,
                      background: "#1e293b",
                      border: "1px solid #3b82f6",
                      color: "#60a5fa",
                      padding: "4px 10px",
                      borderRadius: 6,
                      fontSize: "0.8rem",
                      fontWeight: 600,
                      marginTop: 6
                    }}>
                      <span>⏳ Generazione risposta con Ollama...</span>
                    </div>
                  )}
                </div>
              )}

              {/* FILE ACTIONS & IMPACTED FILES */}
              {msg.fileActions && msg.fileActions.length > 0 && (
                <div style={{ marginTop: 12, paddingTop: 10, borderTop: "1px solid #334155" }}>
                  <div style={{ fontSize: "0.82rem", fontWeight: 600, color: "#38bdf8", marginBottom: 6 }}>
                    🛠️ Operazioni File Progetto:
                  </div>
                  <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                    {msg.fileActions.map((act) => (
                      <div key={act.file} style={{ display: "flex", alignItems: "center", justifyContent: "space-between", background: "#0f172a", padding: "6px 10px", borderRadius: 6, border: "1px solid #334155" }}>
                        <span style={{ fontFamily: "monospace", fontSize: "0.82rem", color: act.action === "delete" ? "#fca5a5" : "#38bdf8" }}>
                          {act.action === "delete" ? "🗑️ DELETE" : "📝 WRITE"}: {act.file}
                          {act.applied && <span style={{ color: "#34d399", marginLeft: 8 }}>[Applicato su Disco]</span>}
                        </span>
                        <div style={{ display: "flex", gap: 6 }}>
                          <button
                            type="button"
                            className="button button--secondary button--small"
                            style={{ fontSize: "0.75rem", background: "#0f172a", borderColor: "#38bdf8", color: "#38bdf8" }}
                            onClick={() => void handleOpenDiff(act.file, act.code)}
                            title="Visualizza il confronto diff delle modifiche"
                          >
                            🔍 Diff
                          </button>
                          {act.action === "write" && act.code && (
                            <button
                              type="button"
                              className="button button--secondary button--small"
                              style={{ fontSize: "0.75rem", background: "#0284c7", borderColor: "#38bdf8", color: "#fff" }}
                              onClick={() => void handleApplyCodeToFile(act.file, act.code!)}
                            >
                              💾 Salva su Disco
                            </button>
                          )}
                          {act.action === "delete" && (
                            <button
                              type="button"
                              className="button button--secondary button--small"
                              style={{ fontSize: "0.75rem", background: "#7f1d1d", borderColor: "#ef4444", color: "#fca5a5" }}
                              onClick={() => void handleDeleteWorkspaceFile(act.file)}
                            >
                              🗑️ Elimina
                            </button>
                          )}
                          <button
                            type="button"
                            className="button button--secondary button--small"
                            style={{ fontSize: "0.75rem" }}
                            onClick={() => void handleOpenExternalFile(act.file)}
                          >
                            🔗 Apri
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ))
        )}
      </div>

      {/* FULL-WIDTH PROMPT CONTAINER */}
      <div
        className="full-width-prompt-card"
        style={{
          width: "100%",
          background: "#0f172a",
          border: "1px solid #334155",
          borderRadius: 10,
          padding: 12,
          display: "flex",
          flexDirection: "column",
          gap: 10,
          boxShadow: "0 4px 16px rgba(0,0,0,0.2)"
        }}
      >
        {/* ATTACHED FILE CHIP BAR */}
        {selectedWorkspaceFile && (
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              background: "#1e293b",
              border: "1px solid #0284c7",
              padding: "6px 12px",
              borderRadius: 6
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
              <span style={{ fontSize: "0.85rem", color: "#38bdf8", fontWeight: 600 }}>📄 Allegato attivo: {selectedWorkspaceFile}</span>
              {attachedFileContent && (
                <span style={{ fontSize: "0.75rem", color: "#94a3b8" }}>
                  ({attachedFileContent.length} char)
                </span>
              )}
            </div>
            <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ fontSize: "0.78rem", background: "#0284c7", borderColor: "#38bdf8", color: "#fff" }}
                onClick={() => setIsAttachedFileEditorOpen(true)}
                title="Apri editor per modificare il contenuto"
              >
                ✏️ Modifica File
              </button>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ fontSize: "0.78rem" }}
                onClick={() => setIsWorkspaceFilePickerOpen(true)}
              >
                📄 Cambia File
              </button>
              <button
                type="button"
                style={{ background: "none", border: "none", color: "#fca5a5", cursor: "pointer", fontSize: "0.9rem" }}
                onClick={() => {
                  setSelectedWorkspaceFile(null);
                  setAttachedFileContent(null);
                }}
                title="Rimuovi allegato"
              >
                ✕
              </button>
            </div>
          </div>
        )}

        {/* FULL-WIDTH PROMPT TEXTAREA */}
        <textarea
          className="input-control"
          style={{
            width: "100%",
            background: "#060911",
            border: "1px solid #334155",
            color: "#f8fafc",
            borderRadius: 8,
            padding: 12,
            fontFamily: "inherit",
            fontSize: "0.92rem",
            lineHeight: 1.5,
            resize: "vertical"
          }}
          rows={3}
          placeholder={
            useAgentMode
              ? "Modalità AGENTE AUTONOMO: Inserisci l'obiettivo (es: analizza il progetto, correggi il bug ed esegui i test)..."
              : operatingMode === "plan"
                ? "Modalità PIANO: Descrivi l'analisi, i flussi o l'architettura da pianificare..."
                : "Modalità SCRITTURA: Descrivi le modifiche al codice, i file da creare/eliminare o chiedi di compilare ed avviare l'app..."
          }
          value={promptInput}
          onChange={(e) => setPromptInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
              e.preventDefault();
              void handleSendMessage();
            }
          }}
          disabled={isGenerating}
        />

        {/* PROMPT ACTION TOOLBAR */}
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: 10 }}>
          {/* MODE TOGGLE SWITCH: LETTURA / PIANO vs SCRITTURA vs AGENTE */}
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
            <span style={{ fontSize: "0.82rem", color: "#94a3b8", fontWeight: 600 }}>Modalità:</span>
            <div style={{ display: "inline-flex", background: "#090d16", padding: 3, borderRadius: 8, border: "1px solid #334155" }}>
              <button
                type="button"
                style={{
                  padding: "5px 12px",
                  borderRadius: 6,
                  border: "none",
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  cursor: "pointer",
                  background: !useAgentMode && operatingMode === "plan" ? "#312e81" : "transparent",
                  color: !useAgentMode && operatingMode === "plan" ? "#c7d2fe" : "#94a3b8",
                  transition: "all 0.15s ease"
                }}
                onClick={() => { setOperatingMode("plan"); setUseAgentMode(false); }}
                title="Modalità Piano/Lettura: analizza e pianifica senza modificare direttamente i file"
              >
                📖 Lettura / Piano
              </button>
              <button
                type="button"
                style={{
                  padding: "5px 12px",
                  borderRadius: 6,
                  border: "none",
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  cursor: "pointer",
                  background: !useAgentMode && operatingMode === "write" ? "#1e3a8a" : "transparent",
                  color: !useAgentMode && operatingMode === "write" ? "#60a5fa" : "#94a3b8",
                  transition: "all 0.15s ease"
                }}
                onClick={() => { setOperatingMode("write"); setUseAgentMode(false); }}
                title="Modalità Scrittura: crea, modifica o elimina file nel workspace ed esegui comandi"
              >
                ✍️ Scrittura
              </button>
              <button
                type="button"
                style={{
                  padding: "5px 12px",
                  borderRadius: 6,
                  border: "none",
                  fontSize: "0.82rem",
                  fontWeight: 600,
                  cursor: "pointer",
                  background: useAgentMode ? "#064e3b" : "transparent",
                  color: useAgentMode ? "#6ee7b7" : "#94a3b8",
                  transition: "all 0.15s ease"
                }}
                onClick={() => setUseAgentMode(true)}
                title="Modalità Agente Autonomo: esplora il progetto, legge file, applica modifiche ed esegue comandi in loop"
              >
                🤖 Agente
              </button>
            </div>

            {useAgentMode && (
              <>
                <label style={{ display: "flex", alignItems: "center", gap: 5, fontSize: "0.78rem", color: "#94a3b8", cursor: "pointer" }}>
                  <input
                    type="checkbox"
                    checked={autoApproveCommands}
                    onChange={(e) => setAutoApproveCommands(e.target.checked)}
                    style={{ accentColor: "#10b981" }}
                  />
                  Auto-Approva Comandi
                </label>
              </>
            )}

            {workspaceConfig?.isAuthorized && !selectedWorkspaceFile && (
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ fontSize: "0.8rem" }}
                onClick={() => setIsWorkspaceFilePickerOpen(true)}
              >
                📎 Allega File Progetto
              </button>
            )}
          </div>

          <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
            {messages.length > 0 && (
              <button
                type="button"
                className="button button--secondary button--small"
                onClick={handleClearMessages}
                title="Svuota la chat ed interrompe eventuali generazioni in corso"
              >
                Pulisci Chat
              </button>
            )}
            {isGenerating ? (
              <button
                type="button"
                className="button button--danger button--small"
                style={{ background: "#991b1b", borderColor: "#ef4444", color: "#ffffff", fontWeight: 600 }}
                onClick={handleCancelGeneration}
              >
                ⛔ Interrompi Risposta
              </button>
            ) : (
              <button
                type="button"
                className="button button--primary"
                style={{ background: "#2563eb", borderColor: "#3b82f6", minWidth: 140 }}
                disabled={!promptInput.trim()}
                onClick={() => void handleSendMessage()}
              >
                ⚡ Invia (Ctrl+Enter)
              </button>
            )}
          </div>
        </div>
      </div>

      {/* ADD CUSTOM PRESET MODAL */}
      {showAddPresetModal && (
        <div className="modal-backdrop" onClick={() => setShowAddPresetModal(false)}>
          <div className="modal-content animate-fade-in" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 500 }}>
            <div className="modal-header">
              <h3>➕ Aggiungi Preset Personalizzato</h3>
              <button type="button" className="button-secondary" onClick={() => setShowAddPresetModal(false)}>✕</button>
            </div>
            <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              <div className="form-field">
                <label className="field-label">Titolo Pulsante:</label>
                <input
                  type="text"
                  className="input-control"
                  placeholder="Es. Valida Form, Aggiungi Logging..."
                  value={newPresetTitle}
                  onChange={(e) => setNewPresetTitle(e.target.value)}
                />
              </div>

              <div className="form-field">
                <label className="field-label">Icona Emoji:</label>
                <input
                  type="text"
                  className="input-control"
                  placeholder="Es. 🛠️, ⚡, 🛡️"
                  value={newPresetIcon}
                  onChange={(e) => setNewPresetIcon(e.target.value)}
                />
              </div>

              <div className="form-field">
                <label className="field-label">Prompt Predefinito:</label>
                <textarea
                  className="input-control"
                  rows={4}
                  placeholder="Testo del prompt da incollare automaticamente..."
                  value={newPresetPrompt}
                  onChange={(e) => setNewPresetPrompt(e.target.value)}
                />
              </div>

              <div style={{ display: "flex", justifyContent: "flex-end", gap: 8, marginTop: 8 }}>
                <button
                  type="button"
                  className="button button--secondary"
                  onClick={() => setShowAddPresetModal(false)}
                >
                  Annulla
                </button>
                <button
                  type="button"
                  className="button button--primary"
                  disabled={!newPresetTitle.trim() || !newPresetPrompt.trim()}
                  onClick={handleAddPreset}
                >
                  Salva Preset
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* WORKSPACE FILE PICKER MODAL */}
      <WorkspaceFilePickerModal
        isOpen={isWorkspaceFilePickerOpen}
        onClose={() => setIsWorkspaceFilePickerOpen(false)}
        files={workspaceFiles}
        onSelectFile={(relPath) => void handleAttachWorkspaceFile(relPath)}
      />

      {/* ATTACHED FILE EDITOR MODAL */}
      <AttachedFileEditorModal
        isOpen={isAttachedFileEditorOpen}
        onClose={() => setIsAttachedFileEditorOpen(false)}
        fileName={selectedWorkspaceFile}
        content={attachedFileContent || ""}
        onSaveContent={(updatedContent, saveToDisk) => void handleSaveAttachedFileContent(updatedContent, saveToDisk)}
      />



      {/* DIFF VIEWER MODAL */}
      <DiffViewerModal
        isOpen={Boolean(diffModalFile)}
        fileName={diffModalFile}
        originalContent={diffModalOriginalContent}
        modifiedContent={diffModalModifiedContent}
        onClose={() => setDiffModalFile(null)}
        onSaveToDisk={
          diffModalFile
            ? () => void handleApplyCodeToFile(diffModalFile, diffModalModifiedContent)
            : undefined
        }
      />
    </section>
  );
}
