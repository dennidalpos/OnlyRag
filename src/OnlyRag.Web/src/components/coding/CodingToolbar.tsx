import { Bot, FileCode, FolderOpen, FolderSearch, X } from "lucide-react";
import type { OllamaModel } from "../../api";
import type { WorkspaceConfig } from "../../apiTypes";

type CodingToolbarProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  selectedModel: string;
  onSelectModel: (model: string) => void;
  workspaceConfig: WorkspaceConfig | null;
  workspaceFilesCount: number;
  onPickFolder: () => void;
  onClearFolder: () => void;
  onOpenFilePicker: () => void;
};

export function CodingToolbar({
  models,
  defaultModel,
  selectedModel,
  onSelectModel,
  workspaceConfig,
  workspaceFilesCount,
  onPickFolder,
  onClearFolder,
  onOpenFilePicker
}: CodingToolbarProps) {
  return (
    <header className="coding-hub-toolbar">
      <div className="coding-hub-toolbar__main">
        <div className="coding-hub-toolbar__title">
          <h2>
            <Bot size={20} style={{ color: "#818cf8" }} /> Coding &amp; Vibe Hub
          </h2>
          <span className="coding-hub-toolbar__subtitle">
            Agent Workbench autonomo per esplorazione, scrittura codice, refactoring e test.
          </span>
        </div>

        <div className="coding-hub-toolbar__actions">
          {workspaceConfig?.isAuthorized ? (
            <div className="workspace-badge-chip" title={workspaceConfig.rootPath ?? ""}>
              <FolderOpen size={16} style={{ color: "#38bdf8", flexShrink: 0 }} />
              <span className="workspace-badge-chip__path">{workspaceConfig.rootPath}</span>
              <span className="workspace-badge-chip__count">
                ({workspaceConfig.fileCount} file)
              </span>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ fontSize: "0.75rem", padding: "2px 8px", display: "inline-flex", alignItems: "center", gap: 4, flexShrink: 0 }}
                onClick={onOpenFilePicker}
              >
                <FileCode size={14} /> File ({workspaceFilesCount})
              </button>
              <button
                type="button"
                className="workspace-badge-chip__remove-btn"
                onClick={onClearFolder}
                title="Rimuovi cartella di progetto"
                aria-label="Rimuovi cartella di progetto"
              >
                <X size={15} />
              </button>
            </div>
          ) : null}

          <button
            type="button"
            className="button button--primary button--small"
            style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
            onClick={onPickFolder}
          >
            <FolderSearch size={16} /> Sfoglia Cartella
          </button>

          <div className="coding-model-group">
            <label htmlFor="coding-model-select" className="coding-model-label">
              Modello:
            </label>
            <select
              id="coding-model-select"
              aria-label="Modello LLM Coding"
              className="coding-model-select"
              value={selectedModel}
              onChange={(e) => onSelectModel(e.target.value)}
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
      </div>
    </header>
  );
}
