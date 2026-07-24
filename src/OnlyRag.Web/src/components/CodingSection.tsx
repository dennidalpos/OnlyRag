import { useEffect, useRef, useState } from "react";
import { apiRequest, apiStreamRequest, type OllamaModel } from "../api";
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
  SelectWorkspaceRequest,
  WorkspaceConfig,
  WorkspaceFileItem,
  WriteWorkspaceFileRequest,
  WriteWorkspaceFileResponse
} from "../apiTypes";
import { formatWorkspaceTreeSummary } from "./CodingSection.helpers";
import {
  deleteCustomPreset,
  loadSavedPresets,
  saveCustomPreset,
  type VibePreset
} from "./CodingSection.storage";
import { AttachedFileEditorModal, WorkspaceFilePickerModal } from "./CodingSection.views";

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
  const [showManualFolderInput, setShowManualFolderInput] = useState(false);
  const [manualFolderPath, setManualFolderPath] = useState("");
  const [workspaceStatusMessage, setWorkspaceStatusMessage] = useState<string | null>(null);

  // Vibe Presets state
  const [presets, setPresets] = useState<VibePreset[]>([]);
  const [showAddPresetModal, setShowAddPresetModal] = useState(false);
  const [newPresetTitle, setNewPresetTitle] = useState("");
  const [newPresetIcon, setNewPresetIcon] = useState("⚡");
  const [newPresetPrompt, setNewPresetPrompt] = useState("");

  const chatContainerRef = useRef<HTMLDivElement | null>(null);

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

  async function handleManualSelectFolder() {
    if (!manualFolderPath.trim()) return;
    try {
      const config = await apiRequest<WorkspaceConfig>("/api/workspace/select", {
        method: "POST",
        body: JSON.stringify({ folderPath: manualFolderPath } as SelectWorkspaceRequest)
      });
      setWorkspaceConfig(config);
      setWorkspaceStatusMessage(`Cartella autorizzata: ${config.rootPath}`);
      setShowManualFolderInput(false);
      setManualFolderPath("");
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceStatusMessage(err instanceof Error ? err.message : "Errore autorizzazione cartella.");
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

  async function handleSendMessage(overridePrompt?: string) {
    const textToSend = overridePrompt ?? promptInput;
    if (!textToSend.trim() || isGenerating) return;

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
        }
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
            <>
              <button
                type="button"
                className="button button--secondary button--small"
                onClick={() => setIsWorkspaceFilePickerOpen(true)}
              >
                📄 File ({workspaceFiles.length})
              </button>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ background: "#065f46", borderColor: "#10b981", color: "#ecfdf5" }}
                onClick={() => void handleExecuteWorkspaceCommand()}
                title="Esegui compilazione dotnet build nella root di progetto"
              >
                🚀 Compila & Avvia
              </button>
            </>
          )}
          <button
            type="button"
            className="button button--primary button--small"
            style={{ background: "#2563eb", borderColor: "#3b82f6" }}
            onClick={() => void handlePickWindowsFolder()}
          >
            📂 Sfoglia Cartella
          </button>
          <button
            type="button"
            className="button button--secondary button--small"
            onClick={() => setShowManualFolderInput(!showManualFolderInput)}
            title="Inserisci percorso manuale"
          >
            ⚙️
          </button>
        </div>
      </div>

      {/* MANUAL FOLDER OVERLAY */}
      {showManualFolderInput && (
        <div style={{ marginBottom: 14, display: "flex", gap: 8, background: "#1e293b", padding: 12, borderRadius: 8, border: "1px solid #475569", width: "100%" }}>
          <input
            type="text"
            className="input-control"
            placeholder="Percorso completo della cartella Windows (es. D:\Progetti\MiaApp)..."
            value={manualFolderPath}
            onChange={(e) => setManualFolderPath(e.target.value)}
          />
          <button
            type="button"
            className="button button--primary button--small"
            onClick={() => void handleManualSelectFolder()}
          >
            Imposta Cartella
          </button>
        </div>
      )}

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
                  background: preset.id.startsWith("preset_audit") ? "#1e1b4b" : "#1e293b",
                  borderColor: preset.id.startsWith("preset_audit") ? "#4338ca" : "#334155",
                  color: preset.id.startsWith("preset_audit") ? "#a5b4fc" : "#f1f5f9",
                  borderRadius: preset.isBuiltIn ? 6 : "6px 0 0 6px",
                  fontWeight: preset.id.startsWith("preset_audit") ? 600 : "normal"
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
                    border: "1px solid #334155",
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
          background: "#090d16",
          borderRadius: 10,
          border: "1px solid #1e293b",
          padding: 16,
          marginBottom: 14,
          display: "flex",
          flexDirection: "column",
          gap: 16
        }}
      >
        {messages.length === 0 ? (
          <div style={{ margin: "auto", textAlign: "center", color: "#64748b", maxWidth: 500 }}>
            <div style={{ fontSize: "2.8rem", marginBottom: 8 }}>🚀</div>
            <h3 style={{ color: "#e2e8f0", marginBottom: 6 }}>Coding & Vibe Hub</h3>
            <p style={{ fontSize: "0.9rem", lineHeight: 1.5, color: "#94a3b8" }}>
              Seleziona la cartella del progetto per consentire all&apos;assistente di analizzare i file, applicare modifiche ed eseguire comandi.
            </p>
          </div>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              style={{
                alignSelf: msg.sender === "user" ? "flex-end" : "flex-start",
                maxWidth: "92%",
                width: "fit-content",
                background: msg.sender === "user" ? "#1e3a8a" : "#1e293b",
                color: "#f8fafc",
                borderRadius: 10,
                padding: "12px 16px",
                border: msg.sender === "user" ? "1px solid #2563eb" : "1px solid #334155",
                boxShadow: "0 2px 8px rgba(0,0,0,0.2)"
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", gap: 12, marginBottom: 6, fontSize: "0.78rem", color: "#94a3b8" }}>
                <strong>{msg.sender === "user" ? "👤 Tu" : `🤖 Assistant (${selectedModel})`}</strong>
                <span>{msg.timestamp}</span>
              </div>

              {msg.attachedFile && (
                <div style={{ background: "#0f172a", padding: "4px 8px", borderRadius: 4, fontSize: "0.8rem", color: "#38bdf8", marginBottom: 8 }}>
                  📄 File allegato: {msg.attachedFile}
                </div>
              )}

              <div style={{ whiteSpace: "pre-wrap", fontFamily: msg.sender === "assistant" ? "monospace" : "inherit", fontSize: "0.9rem", lineHeight: 1.6 }}>
                {msg.content}
                {msg.isStreaming && <span className="streaming-cursor">▌</span>}
              </div>

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
            background: "#090d16",
            border: "1px solid #1e293b",
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
            operatingMode === "plan"
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
          {/* MODE TOGGLE SWITCH: LETTURA / PIANO vs SCRITTURA */}
          <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
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
                  background: operatingMode === "plan" ? "#312e81" : "transparent",
                  color: operatingMode === "plan" ? "#c7d2fe" : "#94a3b8",
                  transition: "all 0.15s ease"
                }}
                onClick={() => setOperatingMode("plan")}
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
                  background: operatingMode === "write" ? "#1e3a8a" : "transparent",
                  color: operatingMode === "write" ? "#60a5fa" : "#94a3b8",
                  transition: "all 0.15s ease"
                }}
                onClick={() => setOperatingMode("write")}
                title="Modalità Scrittura: crea, modifica o elimina file nel workspace ed esegui comandi"
              >
                ✍️ Scrittura
              </button>
            </div>

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
                onClick={() => setMessages([])}
                disabled={isGenerating}
              >
                Pulisci Chat
              </button>
            )}
            <button
              type="button"
              className="button button--primary"
              style={{ background: "#2563eb", borderColor: "#3b82f6", minWidth: 140 }}
              disabled={isGenerating || !promptInput.trim()}
              onClick={() => void handleSendMessage()}
            >
              {isGenerating ? "Generazione in corso..." : "⚡ Invia (Ctrl+Enter)"}
            </button>
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
    </section>
  );
}
