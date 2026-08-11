import { Bot, FileCode, FilePlus, FolderOpen, FolderSearch, X } from "lucide-react";
import type { OllamaModel } from "../../api";
import type { WorkspaceConfig } from "../../apiTypes";
import type { SingleAnalysisFile } from "./useWorkspaceManager";

type CodingToolbarProps = {
  models: OllamaModel[];
  defaultModel: string | null;
  selectedModel: string;
  onSelectModel: (model: string) => void;
  workspaceConfig: WorkspaceConfig | null;
  workspaceFilesCount: number;
  singleFiles?: SingleAnalysisFile[];
  onPickFolder: () => void;
  onClearFolder: () => void;
  onOpenFilePicker: () => void;
  onAddSingleFiles?: () => void;
  onRemoveSingleFile?: (id: string) => void;
  onClearSingleFiles?: () => void;
};

export function CodingToolbar({
  models,
  defaultModel,
  selectedModel,
  onSelectModel,
  workspaceConfig,
  workspaceFilesCount,
  singleFiles = [],
  onPickFolder,
  onClearFolder,
  onOpenFilePicker,
  onAddSingleFiles,
  onRemoveSingleFile,
  onClearSingleFiles
}: CodingToolbarProps) {
  return (
    <header className="coding-hub-toolbar">
      <div className="coding-hub-toolbar__main">
        <div className="coding-hub-toolbar__title">
          <h2>
            <Bot size={20} style={{ color: "#818cf8" }} /> Coding
          </h2>
          <span className="coding-hub-toolbar__subtitle">
            Esplora, modifica e verifica il workspace autorizzato.
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

          {onAddSingleFiles && (
            <button
              type="button"
              className="button button--secondary button--small"
              style={{ display: "inline-flex", alignItems: "center", gap: 6, background: "rgba(56, 189, 248, 0.12)", borderColor: "#38bdf8", color: "#38bdf8" }}
              onClick={onAddSingleFiles}
              title="Aggiungi singoli file arbitrari per l'analisi senza selezionare una cartella di progetto"
            >
              <FilePlus size={16} /> Aggiungi File...
            </button>
          )}

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

      {/* Ad-hoc Single Files List Bar */}
      {singleFiles.length > 0 && (
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 10, padding: "6px 12px", background: "#0f172a", borderRadius: 8, border: "1px solid #1e293b", flexWrap: "wrap" }}>
          <span style={{ fontSize: "0.78rem", fontWeight: 600, color: "#94a3b8" }}>File Analisi Ad-hoc ({singleFiles.length}):</span>
          {singleFiles.map((file) => (
            <div
              key={file.id}
              style={{
                display: "inline-flex",
                alignItems: "center",
                gap: 6,
                padding: "2px 8px",
                background: "rgba(56, 189, 248, 0.15)",
                border: "1px solid rgba(56, 189, 248, 0.3)",
                borderRadius: 4,
                fontSize: "0.78rem",
                color: "#f8fafc"
              }}
            >
              <FileCode size={13} style={{ color: "#38bdf8" }} />
              <span style={{ fontFamily: "monospace" }}>{file.name}</span>
              <span style={{ opacity: 0.6, fontSize: "0.72rem" }}>({(file.sizeBytes / 1024).toFixed(1)} KB)</span>
              {onRemoveSingleFile && (
                <button
                  type="button"
                  onClick={() => onRemoveSingleFile(file.id)}
                  style={{ background: "none", border: "none", color: "#94a3b8", cursor: "pointer", padding: 0, display: "flex", alignItems: "center" }}
                  title="Rimuovi file dalla sessione"
                >
                  <X size={13} />
                </button>
              )}
            </div>
          ))}
          {onClearSingleFiles && singleFiles.length > 1 && (
            <button
              type="button"
              onClick={onClearSingleFiles}
              style={{ fontSize: "0.72rem", color: "#ef4444", background: "none", border: "none", cursor: "pointer", marginLeft: "auto" }}
            >
              Rimuovi Tutti
            </button>
          )}
        </div>
      )}
    </header>
  );
}
