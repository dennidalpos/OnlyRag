import type { OllamaModel } from "../api";
import type { WorkspaceConfig } from "../apiTypes";

type CodingToolbarProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  selectedModel: string;
  onSelectModel: (model: string) => void;
  workspaceConfig: WorkspaceConfig | null;
  workspaceFilesCount: number;
  onPickFolder: () => void;
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
  onOpenFilePicker
}: CodingToolbarProps) {
  return (
    <header className="coding-hub-toolbar">
      <div className="coding-hub-toolbar__main">
        <div className="coding-hub-toolbar__title">
          <h2>
            <span>🤖</span> Coding &amp; Vibe Hub
          </h2>
          <span className="coding-hub-toolbar__subtitle">
            Agent Workbench autonomo per esplorazione, scrittura codice, refactoring e test.
          </span>
        </div>

        <div className="coding-hub-toolbar__actions">
          {workspaceConfig?.isAuthorized ? (
            <div className="workspace-badge-chip" title={workspaceConfig.rootPath ?? ""}>
              <span>📂</span>
              <span className="workspace-badge-chip__path">{workspaceConfig.rootPath}</span>
              <span className="workspace-badge-chip__count">
                ({workspaceConfig.fileCount} file)
              </span>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ fontSize: "0.75rem", padding: "2px 6px" }}
                onClick={onOpenFilePicker}
              >
                📄 File ({workspaceFilesCount})
              </button>
            </div>
          ) : null}

          <button
            type="button"
            className="button button--primary button--small"
            onClick={onPickFolder}
          >
            📂 Sfoglia Cartella
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
