import {
  BookOpen,
  Edit3,
  FileCode,
  Paperclip,
  PenTool,
  Send,
  Sparkles,
  Square,
  Trash2,
  X
} from "lucide-react";

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
  operatingMode,
  onSelectOperatingMode,
  autoApproveCommands,
  onToggleAutoApproveCommands,
  workspaceConfigAuthorized,
  messagesCount,
  isGenerating,
  onSendMessage,
  onCancelGeneration,
  onClearMessages
}: CodingPromptBarProps) {
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
        placeholder={
          operatingMode === "plan"
            ? "Modalità PIANO: Descrivi l'analisi, i flussi o l'architettura da pianificare (l'agente non applicherà modifiche sul disco)..."
            : "Modalità AGENTE SCRITTURA: Inserisci l'obiettivo (es: esplora il progetto, applica le modifiche, esegui i test ed auto-correggi)..."
        }
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
        {/* MODE TOGGLE SWITCH: PIANO vs SCRITTURA */}
        <div className="segmented-mode-selector">
          <span className="segmented-mode-label">Modalità:</span>
          <div className="segmented-mode-group">
            <button
              type="button"
              className={`segmented-mode-button ${operatingMode === "plan" ? "segmented-mode-button--plan-active" : ""}`}
              style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={() => onSelectOperatingMode("plan")}
              title="Modalità Piano/Lettura: analizza e pianifica senza modificare direttamente i file"
            >
              <BookOpen size={14} /> Lettura / Piano
            </button>
            <button
              type="button"
              className={`segmented-mode-button ${operatingMode === "write" ? "segmented-mode-button--write-active" : ""}`}
              style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
              onClick={() => onSelectOperatingMode("write")}
              title="Modalità Scrittura: esplora il progetto, crea/modifica file nel workspace ed esegui comandi in loop"
            >
              <PenTool size={14} /> Agente Scrittura
            </button>
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
