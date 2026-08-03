import {
  Edit3,
  FileCode,
  Paperclip,
  Sparkles,
  Square,
  Trash2,
  X
} from "lucide-react";
import { useSmartIntentRouter } from "./useSmartIntentRouter";

type CodingPromptBarProps = {
  selectedWorkspaceFile: string | null;
  attachedFileContent: string | null;
  onOpenAttachedFileEditor: () => void;
  onOpenWorkspaceFilePicker: () => void;
  onRemoveAttachedFile: () => void;
  promptInput: string;
  onPromptInputChange: (value: string) => void;
  operatingMode: "plan" | "write";
  onSelectOperatingMode: (mode: "plan" | "write") => void;
  autoApproveCommands: boolean;
  onToggleAutoApproveCommands: (autoApprove: boolean) => void;
  workspaceConfigAuthorized: boolean;
  messagesCount: number;
  isGenerating: boolean;
  onSendMessage: () => void;
  onCancelGeneration: () => void;
  onClearMessages: () => void;
};

export function CodingPromptBar({
  selectedWorkspaceFile,
  attachedFileContent,
  onOpenAttachedFileEditor,
  onOpenWorkspaceFilePicker,
  onRemoveAttachedFile,
  promptInput,
  onPromptInputChange,
  operatingMode: _operatingMode,
  onSelectOperatingMode: _onSelectOperatingMode,
  autoApproveCommands,
  onToggleAutoApproveCommands,
  workspaceConfigAuthorized,
  messagesCount,
  isGenerating,
  onSendMessage,
  onCancelGeneration,
  onClearMessages
}: CodingPromptBarProps) {
  const intentMeta = useSmartIntentRouter({
    promptInput,
    selectedWorkspaceFile,
    attachedFileContent
  });

  return (
    <div className="coding-prompt-card">
      {/* ATTACHED FILE CHIP BAR */}
      {selectedWorkspaceFile && (
        <div className="attached-file-chip-bar">
          <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
            <FileCode size={16} style={{ color: "#38bdf8" }} />
            <span className="attached-file-chip-title">Allegato attivo: {selectedWorkspaceFile}</span>
            {attachedFileContent && (
              <span className="attached-file-chip-size">
                ({attachedFileContent.length} char)
              </span>
            )}
          </div>
          <div className="attached-file-chip-actions">
            <button
              type="button"
              className="button button--secondary button--small"
              style={{ fontSize: "0.78rem", display: "inline-flex", alignItems: "center", gap: 4 }}
              onClick={onOpenAttachedFileEditor}
              title="Apri editor per modificare il contenuto"
            >
              <Edit3 size={13} /> Modifica File
            </button>
            <button
              type="button"
              className="button button--secondary button--small"
              style={{ fontSize: "0.78rem", display: "inline-flex", alignItems: "center", gap: 4 }}
              onClick={onOpenWorkspaceFilePicker}
            >
              <FileCode size={13} /> Cambia File
            </button>
            <button
              type="button"
              className="attached-file-remove-btn"
              onClick={onRemoveAttachedFile}
              title="Rimuovi allegato"
            >
              <X size={16} />
            </button>
          </div>
        </div>
      )}

      {/* FULL-WIDTH PROMPT TEXTAREA */}
      <textarea
        className="coding-prompt-textarea"
        rows={3}
        placeholder="Inserisci l'obiettivo o la domanda (es: analizza il codice, crea una feature, applica modifiche o risolvi un bug)..."
        value={promptInput}
        onChange={(e) => onPromptInputChange(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            onSendMessage();
          }
        }}
        disabled={isGenerating}
      />

      {/* PROMPT ACTION TOOLBAR */}
      <div className="coding-prompt-actions">
        {/* SMART INTENT BADGE & MODE SELECTOR */}
        <div className="segmented-mode-selector">
          <div
            className="smart-intent-badge"
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              padding: "4px 10px",
              borderRadius: 12,
              backgroundColor: "rgba(255, 255, 255, 0.05)",
              border: `1px solid ${intentMeta.badgeColor}`,
              color: intentMeta.badgeColor,
              fontSize: "0.78rem",
              fontWeight: 600
            }}
            title={intentMeta.description}
          >
            {intentMeta.label}
          </div>

          <label className="auto-approve-checkbox-label">
            <input
              type="checkbox"
              checked={autoApproveCommands}
              onChange={(e) => onToggleAutoApproveCommands(e.target.checked)}
            />
            Auto-Approva Comandi
          </label>

          {workspaceConfigAuthorized && !selectedWorkspaceFile && (
            <button
              type="button"
              className="button button--secondary button--small"
              style={{ fontSize: "0.8rem", display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={onOpenWorkspaceFilePicker}
            >
              <Paperclip size={14} /> Allega File Progetto
            </button>
          )}
        </div>

        <div className="coding-prompt-buttons">
          {messagesCount > 0 && (
            <button
              type="button"
              className="button button--secondary button--small"
              style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={onClearMessages}
              title="Svuota la chat ed interrompe eventuali generazioni in corso"
            >
              <Trash2 size={14} /> Pulisci Chat
            </button>
          )}
          {isGenerating ? (
            <button
              type="button"
              className="button button--danger button--small"
              style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={onCancelGeneration}
            >
              <Square size={14} /> Interrompi Risposta
            </button>
          ) : (
            <button
              type="button"
              className="button button--primary"
              style={{ minWidth: 140, display: "inline-flex", alignItems: "center", justifyContent: "center", gap: 6 }}
              disabled={!promptInput.trim()}
              onClick={onSendMessage}
            >
              <Sparkles size={16} /> Invia (Ctrl+Enter)
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
