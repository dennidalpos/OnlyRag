import { CheckCircle2, FileCode, FileDiff, RotateCcw, Save, Search, X } from "lucide-react";
import { useEffect, useState } from "react";
import type { WorkspaceFileItem } from "../../apiTypes";
import { computeCompactDiff, computeLineDiff } from "./CodingSection.helpers";

type WorkspaceFilePickerModalProps = {
  isOpen: boolean;
  onClose: () => void;
  files: WorkspaceFileItem[];
  onSelectFile: (relativePath: string) => void;
};

export function WorkspaceFilePickerModal({
  isOpen,
  onClose,
  files,
  onSelectFile
}: WorkspaceFilePickerModalProps) {
  const [searchFilter, setSearchFilter] = useState("");
  if (!isOpen) return null;

  const filteredFiles = files.filter(
    (f) => !f.isDirectory && f.relativePath.toLowerCase().includes(searchFilter.toLowerCase())
  );

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-content animate-fade-in" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 650 }}>
        <div className="modal-header">
          <h3 style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <FileCode size={20} style={{ color: "#38bdf8" }} /> Seleziona File dal Progetto Autorizzato
          </h3>
          <button type="button" className="button-secondary" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ position: "relative" }}>
            <Search size={16} style={{ position: "absolute", left: 12, top: 12, color: "#64748b" }} />
            <input
              type="text"
              className="input-control"
              style={{ paddingLeft: 36 }}
              placeholder="Filtra file per nome o percorso (es: Controller.cs, App.tsx)..."
              value={searchFilter}
              onChange={(e) => setSearchFilter(e.target.value)}
            />
          </div>
          <div className="file-list-container" style={{ maxHeight: 350, overflowY: "auto", background: "#0f172a", borderRadius: 8, padding: 8 }}>
            {filteredFiles.length === 0 ? (
              <p style={{ color: "#94a3b8", textAlign: "center", padding: 16 }}>Nessun file trovato</p>
            ) : (
              filteredFiles.map((file) => (
                <div
                  key={file.relativePath}
                  className="file-item-row"
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    alignItems: "center",
                    padding: "8px 12px",
                    borderBottom: "1px solid #1e293b",
                    cursor: "pointer"
                  }}
                  onClick={() => onSelectFile(file.relativePath)}
                >
                  <span style={{ fontFamily: "monospace", fontSize: "0.88rem", color: "#38bdf8", display: "inline-flex", alignItems: "center", gap: 6 }}>
                    <FileCode size={14} /> {file.relativePath}
                  </span>
                  <span style={{ fontSize: "0.75rem", color: "#64748b" }}>{(file.sizeBytes / 1024).toFixed(1)} KB</span>
                </div>
              ))
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

type AttachedFileEditorModalProps = {
  isOpen: boolean;
  onClose: () => void;
  fileName: string | null;
  content: string;
  onSaveContent: (updatedContent: string, saveToDisk: boolean) => void;
};

export function AttachedFileEditorModal({
  isOpen,
  onClose,
  fileName,
  content,
  onSaveContent
}: AttachedFileEditorModalProps) {
  const [editedText, setEditedText] = useState(content);

  useEffect(() => {
    setEditedText(content);
  }, [content, isOpen]);

  if (!isOpen || !fileName) return null;

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-content animate-fade-in" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 800, width: "90vw" }}>
        <div className="modal-header">
          <h3 style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <FileCode size={20} style={{ color: "#38bdf8" }} /> Modifica File Allegato: <span style={{ fontFamily: "monospace", color: "#38bdf8" }}>{fileName}</span>
          </h3>
          <button type="button" className="button-secondary" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <p style={{ fontSize: "0.85rem", color: "#94a3b8", margin: 0 }}>
            Puoi modificare liberamente il codice qui sotto per aggiungere/togliere funzioni, correggere errori o adattare il contesto prima dell&apos;invio.
          </p>

          <textarea
            className="input-control"
            style={{
              fontFamily: "Consolas, Monaco, monospace",
              fontSize: "0.88rem",
              lineHeight: 1.5,
              background: "#090d16",
              color: "#f8fafc",
              border: "1px solid #334155",
              borderRadius: 8,
              padding: 12
            }}
            rows={16}
            value={editedText}
            onChange={(e) => setEditedText(e.target.value)}
          />

          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 8 }}>
            <div style={{ fontSize: "0.78rem", color: "#64748b" }}>
              Caratteri: {editedText.length} | Righe: {editedText.split("\n").length}
            </div>

            <div style={{ display: "flex", gap: 10 }}>
              <button
                type="button"
                className="button button--secondary button--small"
                onClick={onClose}
              >
                Annulla
              </button>
              <button
                type="button"
                className="button button--secondary button--small"
                style={{ background: "#0284c7", borderColor: "#38bdf8", color: "#ffffff", display: "inline-flex", alignItems: "center", gap: 6 }}
                onClick={() => onSaveContent(editedText, false)}
                title="Aggiorna il contesto allegato senza sovrascrivere il file sul disco"
              >
                📌 Usa come Contesto Chat
              </button>
              <button
                type="button"
                className="button button--primary button--small"
                style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
                onClick={() => onSaveContent(editedText, true)}
                title="Salva le modifiche direttamente nel file del progetto autorizzato sul tuo PC"
              >
                <Save size={14} /> Salva su Disco &amp; Usa
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

type DiffViewerModalProps = {
  isOpen: boolean;
  onClose: () => void;
  fileName: string | null;
  originalContent: string;
  modifiedContent: string;
  onSaveToDisk?: () => void;
  onRollback?: () => void;
  isAppliedOnDisk?: boolean;
};

export function DiffViewerModal({
  isOpen,
  onClose,
  fileName,
  originalContent,
  modifiedContent,
  onSaveToDisk,
  onRollback,
  isAppliedOnDisk = false
}: DiffViewerModalProps) {
  if (!isOpen || !fileName) return null;

  const fullDiff = computeLineDiff(originalContent, modifiedContent);
  const compactDiff = computeCompactDiff(originalContent, modifiedContent);
  const additions = fullDiff.filter((l) => l.type === "add").length;
  const deletions = fullDiff.filter((l) => l.type === "delete").length;

  const isAlreadyApplied = isAppliedOnDisk || (additions === 0 && deletions === 0);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal-content animate-fade-in" onClick={(e) => e.stopPropagation()} style={{ maxWidth: 900, width: "95vw" }}>
        <div className="modal-header">
          <h3 style={{ display: "flex", alignItems: "center", gap: 8 }}>
            <FileDiff size={20} style={{ color: "#38bdf8" }} />
            <span>Diff Modifiche: <span style={{ fontFamily: "monospace", color: "#38bdf8" }}>{fileName}</span></span>
            <span style={{ fontSize: "0.8rem", marginLeft: 12, display: "inline-flex", alignItems: "center", gap: 6 }}>
              {isAlreadyApplied ? (
                <span style={{ color: "#34d399", background: "rgba(52,211,153,0.15)", padding: "2px 8px", borderRadius: 4, display: "inline-flex", alignItems: "center", gap: 4 }}>
                  <CheckCircle2 size={13} /> Applicato su Disco
                </span>
              ) : (
                <>
                  <span style={{ color: "#34d399", fontWeight: 700 }}>+{additions}</span>
                  <span style={{ color: "#f87171", fontWeight: 700 }}>-{deletions}</span>
                </>
              )}
            </span>
          </h3>
          <button type="button" className="button-secondary" onClick={onClose}><X size={16} /></button>
        </div>
        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          {isAlreadyApplied && (
            <div style={{ background: "#064e3b", border: "1px solid #10b981", color: "#a7f3d0", padding: "8px 12px", borderRadius: 6, fontSize: "0.84rem", display: "flex", alignItems: "center", gap: 8 }}>
              <CheckCircle2 size={16} style={{ color: "#34d399", flexShrink: 0 }} />
              <span><strong>Modifiche già applicate sul disco.</strong> Di seguito sono mostrate solo le righe modificate a confronto con le originali.</span>
            </div>
          )}

          <div
            style={{
              maxHeight: 480,
              overflowY: "auto",
              background: "#090d16",
              borderRadius: 8,
              padding: 12,
              fontFamily: "Consolas, Monaco, monospace",
              fontSize: "0.85rem",
              lineHeight: 1.5,
              border: "1px solid #1e293b"
            }}
          >
            {compactDiff.map((line, idx) => {
              if (line.type === "hunk_separator") {
                return (
                  <div
                    key={idx}
                    style={{
                      background: "rgba(56, 189, 248, 0.08)",
                      color: "#38bdf8",
                      padding: "4px 8px",
                      margin: "4px 0",
                      borderRadius: 4,
                      fontSize: "0.78rem",
                      fontWeight: 600,
                      textAlign: "center"
                    }}
                  >
                    {line.content}
                  </div>
                );
              }

              const bg = line.type === "add"
                ? "rgba(52, 211, 153, 0.15)"
                : line.type === "delete"
                  ? "rgba(248, 113, 113, 0.15)"
                  : "transparent";
              const color = line.type === "add"
                ? "#34d399"
                : line.type === "delete"
                  ? "#f87171"
                  : "#94a3b8";
              const prefix = line.type === "add" ? "+" : line.type === "delete" ? "-" : " ";

              return (
                <div
                  key={idx}
                  style={{
                    background: bg,
                    color: color,
                    display: "grid",
                    gridTemplateColumns: "40px 40px 18px 1fr",
                    gap: 8,
                    padding: "1px 4px",
                    borderRadius: 2
                  }}
                >
                  <span style={{ opacity: 0.4, textAlign: "right", userSelect: "none" }}>
                    {line.oldLineNumber ?? ""}
                  </span>
                  <span style={{ opacity: 0.4, textAlign: "right", userSelect: "none" }}>
                    {line.newLineNumber ?? ""}
                  </span>
                  <span style={{ fontWeight: 700, userSelect: "none" }}>{prefix}</span>
                  <span style={{ whiteSpace: "pre-wrap", wordBreak: "break-all" }}>{line.content}</span>
                </div>
              );
            })}
          </div>

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 8 }}>
            <button type="button" className="button button--secondary button--small" onClick={onClose}>
              Chiudi
            </button>
            {onRollback && originalContent && (
              <button
                type="button"
                className="button button--danger button--small"
                style={{ background: "#7f1d1d", color: "#fca5a5", border: "1px solid #ef4444", display: "inline-flex", alignItems: "center", gap: 6 }}
                onClick={() => {
                  onRollback();
                  onClose();
                }}
              >
                <RotateCcw size={14} /> Ripristina Versione Originale
              </button>
            )}
            {!isAlreadyApplied && onSaveToDisk && (
              <button
                type="button"
                className="button button--primary button--small"
                style={{ display: "inline-flex", alignItems: "center", gap: 6 }}
                onClick={() => {
                  onSaveToDisk();
                  onClose();
                }}
              >
                <Save size={14} /> Applica Modifiche su Disco
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
