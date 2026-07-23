import { useEffect, useState, type RefObject } from "react";
import type { WorkspaceConfig, WorkspaceFileItem } from "../apiTypes";
import { generateHtmlSandboxDoc, personaOptions } from "./CodingSection.helpers";
import type { CodeSnippetItem, RefactorGoal, VibePersona } from "./CodingSection.types";

type WorkbenchViewProps = {
  selectedPersona: VibePersona;
  onSelectPersona: (persona: VibePersona) => void;
  selectedLanguage: string;
  onSelectLanguage: (lang: string) => void;
  prompt: string;
  onPromptChange: (val: string) => void;
  codeContext: string;
  onCodeContextChange: (val: string) => void;
  generatedCode: string;
  explanation: string;
  executionSuggestions: string[];
  isGenerating: boolean;
  onGenerate: () => void;
  onSaveSnippet: (title: string, code: string, lang: string, exp?: string) => void;
  onOpenPreview: (code: string) => void;
  codeOutputRef?: RefObject<HTMLPreElement | null>;
  workspaceConfig?: WorkspaceConfig | null;
  selectedWorkspaceFile?: string | null;
  onOpenWorkspacePicker?: () => void;
  onApplyToWorkspace?: (relativePath: string, content: string) => void;
};

export function WorkbenchView({
  selectedPersona,
  onSelectPersona,
  selectedLanguage,
  onSelectLanguage,
  prompt,
  onPromptChange,
  codeContext,
  onCodeContextChange,
  generatedCode,
  explanation,
  executionSuggestions,
  isGenerating,
  onGenerate,
  onSaveSnippet,
  onOpenPreview,
  codeOutputRef,
  workspaceConfig,
  selectedWorkspaceFile,
  onOpenWorkspacePicker,
  onApplyToWorkspace
}: WorkbenchViewProps) {
  const [copied, setCopied] = useState(false);
  const [snippetTitle, setSnippetTitle] = useState("");
  const [showSaveInput, setShowSaveInput] = useState(false);

  function handleCopy() {
    void navigator.clipboard.writeText(generatedCode);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  function handleSave() {
    if (!snippetTitle.trim()) return;
    onSaveSnippet(snippetTitle, generatedCode, selectedLanguage, explanation);
    setSnippetTitle("");
    setShowSaveInput(false);
  }

  return (
    <div className="coding-workbench-grid">
      <div className="coding-panel coding-panel--input">
        <h3 className="coding-panel__title">1. Configura Persona & Intent</h3>

        <div className="persona-selector">
          <label className="field-label">Persona Vibe Coder / Intent:</label>
          <div className="persona-grid">
            {personaOptions.map((opt) => (
              <button
                key={opt.id}
                type="button"
                className={`persona-card ${selectedPersona === opt.id ? "persona-card--active" : ""}`}
                onClick={() => onSelectPersona(opt.id)}
              >
                <strong>{opt.label}</strong>
                <small>{opt.description}</small>
              </button>
            ))}
          </div>
        </div>

        <div className="language-selector form-field">
          <label htmlFor="coding-lang-select" className="field-label">Linguaggio Target:</label>
          <select
            id="coding-lang-select"
            className="input-control"
            value={selectedLanguage}
            onChange={(e) => onSelectLanguage(e.target.value)}
          >
            <option value="csharp">C# (.NET 10)</option>
            <option value="typescript">TypeScript (React 19 / Vite)</option>
            <option value="javascript">JavaScript / HTML Web</option>
            <option value="python">Python</option>
            <option value="sql">SQL (SQLite / PostgreSQL)</option>
            <option value="powershell">PowerShell 7</option>
            <option value="html">HTML5 & CSS3</option>
            <option value="json">JSON Schema</option>
          </select>
        </div>

        <div className="form-field">
          <label htmlFor="coding-prompt-input" className="field-label">Descrivi cosa vuoi creare o modificare (Prompt):</label>
          <textarea
            id="coding-prompt-input"
            className="input-control textarea-control"
            rows={4}
            placeholder={
              selectedPersona === "free_prompt"
                ? "Prompt Libero: inserisci qualsiasi richiesta senza vincoli di formato predefiniti..."
                : "Es: Crea un servizio C# async per la gestione dei task vettoriali con cancellazione e retry policy..."
            }
            value={prompt}
            onChange={(e) => onPromptChange(e.target.value)}
          />
        </div>

        <div className="form-field">
          <div className="field-header-row">
            <label htmlFor="coding-context-input" className="field-label">Contesto Codice Esistente (Opzionale):</label>
            {workspaceConfig?.isAuthorized && onOpenWorkspacePicker && (
              <button
                type="button"
                className="button-link"
                onClick={onOpenWorkspacePicker}
                title="Scegli un file dalla cartella di progetto autorizzata"
              >
                📂 Scegli da Progetto {selectedWorkspaceFile ? `(${selectedWorkspaceFile})` : ""}
              </button>
            )}
          </div>
          <textarea
            id="coding-context-input"
            className="input-control textarea-control"
            rows={4}
            placeholder="Incolla qui i frammenti di codice o seleziona un file dalla cartella di progetto autorizzata..."
            value={codeContext}
            onChange={(e) => onCodeContextChange(e.target.value)}
          />
        </div>

        <button
          type="button"
          className="button button--primary button--full"
          disabled={isGenerating || !prompt.trim()}
          onClick={onGenerate}
        >
          {isGenerating ? "Generazione in corso (Streaming)..." : "⚡ Genera Codice Vibe"}
        </button>
      </div>

      <div className="coding-panel coding-panel--output">
        <div className="coding-panel__header">
          <h3>2. Codice Generato {isGenerating && <span className="streaming-badge">🔴 Streaming...</span>}</h3>
          {generatedCode && (
            <div className="coding-actions-bar">
              <button type="button" className="button button--secondary button--small" onClick={handleCopy}>
                {copied ? "Copiato! ✓" : "Copia Codice"}
              </button>
              {(selectedLanguage === "javascript" || selectedLanguage === "html" || selectedLanguage === "typescript") && (
                <button type="button" className="button button--secondary button--small" onClick={() => onOpenPreview(generatedCode)}>
                  🌐 Anteprima Live
                </button>
              )}
              {workspaceConfig?.isAuthorized && selectedWorkspaceFile && onApplyToWorkspace && (
                <button
                  type="button"
                  className="button button--secondary button--small"
                  onClick={() => onApplyToWorkspace(selectedWorkspaceFile, generatedCode)}
                  title={`Sovrascrivi ${selectedWorkspaceFile} nel progetto`}
                >
                  💾 Applica a {selectedWorkspaceFile}
                </button>
              )}
              <button
                type="button"
                className="button button--secondary button--small"
                onClick={() => setShowSaveInput(!showSaveInput)}
              >
                💾 Salva Snippet
              </button>
            </div>
          )}
        </div>

        {showSaveInput && (
          <div className="save-snippet-popover form-field">
            <input
              type="text"
              className="input-control"
              placeholder="Titolo dello snippet..."
              value={snippetTitle}
              onChange={(e) => setSnippetTitle(e.target.value)}
            />
            <button type="button" className="button button--primary button--small" onClick={handleSave}>
              Salva
            </button>
          </div>
        )}

        {generatedCode ? (
          <div className="code-display-container">
            <pre className="code-block" ref={codeOutputRef}>
              <code>{generatedCode}</code>
            </pre>
            {explanation && (
              <div className="coding-explanation-box">
                <h4>Spiegazione Architetturale</h4>
                <p>{explanation}</p>
              </div>
            )}
            {executionSuggestions.length > 0 && (
              <div className="coding-suggestions-box">
                <h4>Suggerimenti di Esecuzione</h4>
                <ul>
                  {executionSuggestions.map((s, idx) => (
                    <li key={idx}>{s}</li>
                  ))}
                </ul>
              </div>
            )}
          </div>
        ) : (
          <div className="coding-empty-state">
            <p>Seleziona una persona o Prompt Libero, inserisci la richiesta e premi <strong>Genera Codice Vibe</strong> per iniziare.</p>
          </div>
        )}
      </div>
    </div>
  );
}

type RefactorViewProps = {
  originalCode: string;
  onOriginalCodeChange: (val: string) => void;
  refactorGoal: RefactorGoal;
  onRefactorGoalChange: (goal: RefactorGoal) => void;
  refactoredCode: string;
  refactorExplanation: string;
  isGenerating: boolean;
  onRefactor: () => void;
};

export function RefactorView({
  originalCode,
  onOriginalCodeChange,
  refactorGoal,
  onRefactorGoalChange,
  refactoredCode,
  refactorExplanation,
  isGenerating,
  onRefactor
}: RefactorViewProps) {
  const [copied, setCopied] = useState(false);

  function handleCopy() {
    void navigator.clipboard.writeText(refactoredCode);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <div className="coding-refactor-container">
      <div className="coding-refactor-toolbar">
        <div className="form-field">
          <label className="field-label">Obiettivo Refactoring:</label>
          <div className="pill-selector">
            <button
              type="button"
              className={`pill ${refactorGoal === "readability" ? "pill--active" : ""}`}
              onClick={() => onRefactorGoalChange("readability")}
            >
              📖 Leggibilità & Clean Code
            </button>
            <button
              type="button"
              className={`pill ${refactorGoal === "performance" ? "pill--active" : ""}`}
              onClick={() => onRefactorGoalChange("performance")}
            >
              ⚡ Ottimizzazione Performance
            </button>
            <button
              type="button"
              className={`pill ${refactorGoal === "unit_tests" ? "pill--active" : ""}`}
              onClick={() => onRefactorGoalChange("unit_tests")}
            >
              🧪 Genera Unit Tests
            </button>
            <button
              type="button"
              className={`pill ${refactorGoal === "type_safety" ? "pill--active" : ""}`}
              onClick={() => onRefactorGoalChange("type_safety")}
            >
              🛡️ Type Safety & Sicurezza
            </button>
          </div>
        </div>

        <button
          type="button"
          className="button button--primary"
          disabled={isGenerating || !originalCode.trim()}
          onClick={onRefactor}
        >
          {isGenerating ? "Refactoring in corso..." : "🔄 Rifattorizza Codice"}
        </button>
      </div>

      <div className="diff-grid">
        <div className="coding-panel">
          <h4 className="coding-panel__subheading">Codice Originale</h4>
          <textarea
            className="input-control textarea-control code-font"
            rows={14}
            placeholder="Incolla qui il codice sorgente originale..."
            value={originalCode}
            onChange={(e) => onOriginalCodeChange(e.target.value)}
          />
        </div>

        <div className="coding-panel">
          <div className="coding-panel__header">
            <h4 className="coding-panel__subheading">Codice Rifattorizzato</h4>
            {refactoredCode && (
              <button type="button" className="button button--secondary button--small" onClick={handleCopy}>
                {copied ? "Copiato! ✓" : "Copia Risultato"}
              </button>
            )}
          </div>
          {refactoredCode ? (
            <div className="refactored-output">
              <pre className="code-block">
                <code>{refactoredCode}</code>
              </pre>
              {refactorExplanation && (
                <div className="coding-explanation-box">
                  <h4>Miglioramenti Apportati</h4>
                  <p>{refactorExplanation}</p>
                </div>
              )}
            </div>
          ) : (
            <div className="coding-empty-state">
              <p>Il codice ristrutturato apparirà qui con il dettaglio dei miglioramenti.</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

type DiagnoseViewProps = {
  errorLog: string;
  onErrorLogChange: (val: string) => void;
  diagnoseContext: string;
  onDiagnoseContextChange: (val: string) => void;
  rootCause: string;
  suggestedFix: string;
  fixedDiff: string;
  isGenerating: boolean;
  onDiagnose: () => void;
};

export function DiagnoseView({
  errorLog,
  onErrorLogChange,
  diagnoseContext,
  onDiagnoseContextChange,
  rootCause,
  suggestedFix,
  fixedDiff: _fixedDiff,
  isGenerating,
  onDiagnose
}: DiagnoseViewProps) {
  return (
    <div className="coding-diagnose-container">
      <div className="coding-panel">
        <h3>Diagnostica Errore & Stack Trace</h3>
        <div className="form-field">
          <label className="field-label">Errore di Compilazione / Stack Trace / Log di Terminale:</label>
          <textarea
            className="input-control textarea-control code-font"
            rows={5}
            placeholder="Incolla qui l'errore generato da dotnet build, npm run test o eccezione runtime..."
            value={errorLog}
            onChange={(e) => onErrorLogChange(e.target.value)}
          />
        </div>

        <div className="form-field">
          <label className="field-label">Contesto Codice Relativo (Opzionale):</label>
          <textarea
            className="input-control textarea-control code-font"
            rows={4}
            placeholder="Incolla la funzione o il file in cui si verifica l'errore..."
            value={diagnoseContext}
            onChange={(e) => onDiagnoseContextChange(e.target.value)}
          />
        </div>

        <button
          type="button"
          className="button button--primary"
          disabled={isGenerating || !errorLog.trim()}
          onClick={onDiagnose}
        >
          {isGenerating ? "Diagnosi in corso..." : "🔍 Diagnostica Errore"}
        </button>
      </div>

      {rootCause && (
        <div className="coding-panel coding-panel--results">
          <h3>Risultato Diagnosi</h3>

          <div className="diagnose-card diagnose-card--rootcause">
            <h4>Analisi Causa Radice</h4>
            <p>{rootCause}</p>
          </div>

          {suggestedFix && (
            <div className="diagnose-card diagnose-card--fix">
              <h4>Codice Corretto Proposto</h4>
              <pre className="code-block">
                <code>{suggestedFix}</code>
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

type LivePreviewViewProps = {
  code: string;
  language: string;
};

export function LivePreviewView({ code, language }: LivePreviewViewProps) {
  const htmlContent = generateHtmlSandboxDoc(code, language);

  return (
    <div className="coding-preview-container">
      <div className="coding-preview-header">
        <h4>Anteprima Sandbox Live</h4>
        <small>Esecuzione isolata all'interno dell'applicazione desktop</small>
      </div>
      <iframe
        className="sandbox-iframe"
        title="Live Sandbox Preview"
        srcDoc={htmlContent}
        sandbox="allow-scripts"
      />
    </div>
  );
}

type SavedSnippetsDrawerProps = {
  snippets: CodeSnippetItem[];
  onSelectSnippet: (snippet: CodeSnippetItem) => void;
  onDeleteSnippet: (id: string) => void;
};

export function SavedSnippetsDrawer({ snippets, onSelectSnippet, onDeleteSnippet }: SavedSnippetsDrawerProps) {
  if (snippets.length === 0) return null;

  return (
    <aside className="snippets-drawer" aria-label="Snippet Salvati">
      <h4>Libreria Snippet ({snippets.length})</h4>
      <div className="snippets-list">
        {snippets.map((s) => (
          <div key={s.id} className="snippet-item">
            <div className="snippet-item__info">
              <strong>{s.title}</strong>
              <small>{s.language}</small>
            </div>
            <div className="snippet-item__actions">
              <button
                type="button"
                className="button button--secondary button--small"
                onClick={() => onSelectSnippet(s)}
              >
                Usa
              </button>
              <button
                type="button"
                className="button button--danger button--small"
                onClick={() => onDeleteSnippet(s.id)}
              >
                Elimina
              </button>
            </div>
          </div>
        ))}
      </div>
    </aside>
  );
}

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
          <h3>📂 Seleziona File dal Progetto Autorizzato</h3>
          <button type="button" className="button-secondary" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <input
            type="text"
            className="input-control"
            placeholder="Filtra file per nome o percorso (es: Controller.cs, App.tsx)..."
            value={searchFilter}
            onChange={(e) => setSearchFilter(e.target.value)}
          />
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
                  <span style={{ fontFamily: "monospace", fontSize: "0.88rem", color: "#38bdf8" }}>📄 {file.relativePath}</span>
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
          <h3>✏️ Modifica File Allegato: <span style={{ fontFamily: "monospace", color: "#38bdf8" }}>{fileName}</span></h3>
          <button type="button" className="button-secondary" onClick={onClose}>✕</button>
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
                style={{ background: "#0284c7", borderColor: "#38bdf8", color: "#ffffff" }}
                onClick={() => onSaveContent(editedText, false)}
                title="Aggiorna il contesto allegato senza sovrascrivere il file sul disco"
              >
                📌 Usa come Contesto Chat
              </button>
              <button
                type="button"
                className="button button--primary button--small"
                onClick={() => onSaveContent(editedText, true)}
                title="Salva le modifiche direttamente nel file del progetto autorizzato sul tuo PC"
              >
                💾 Salva su Disco & Usa
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
