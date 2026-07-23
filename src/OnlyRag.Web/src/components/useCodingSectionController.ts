import { useEffect, useRef, useState } from "react";
import { apiRequest, apiStreamRequest, type OllamaModel } from "../api";
import type {
  CodeDiagnoseRequest,
  CodeDiagnoseResponse,
  CodeRefactorRequest,
  CodeRefactorResponse,
  CodingTaskRequest,
  ReadWorkspaceFileRequest,
  ReadWorkspaceFileResponse,
  SelectWorkspaceRequest,
  WorkspaceConfig,
  WorkspaceFileItem,
  WriteWorkspaceFileRequest,
  WriteWorkspaceFileResponse
} from "../apiTypes";
import { deleteSnippet, loadSavedSnippets, saveSnippet } from "./CodingSection.storage";
import type { CodeSnippetItem, CodingTab, RefactorGoal, VibePersona } from "./CodingSection.types";

type UseCodingSectionControllerProps = {
  models: OllamaModel[];
  defaultModel: string | null;
};

export function useCodingSectionController({ models, defaultModel }: UseCodingSectionControllerProps) {
  const [activeTab, setActiveTab] = useState<CodingTab>("workbench");
  const [selectedPersona, setSelectedPersona] = useState<VibePersona>("architect");
  const [selectedModel, setSelectedModel] = useState<string>("");
  const [selectedLanguage, setSelectedLanguage] = useState<string>("csharp");

  // Workbench state
  const [prompt, setPrompt] = useState("");
  const [codeContext, setCodeContext] = useState("");
  const [generatedCode, setGeneratedCode] = useState("");
  const [explanation, setExplanation] = useState("");
  const [executionSuggestions, setExecutionSuggestions] = useState<string[]>([]);

  // Refactor state
  const [originalCode, setOriginalCode] = useState("");
  const [refactorGoal, setRefactorGoal] = useState<RefactorGoal>("readability");
  const [refactoredCode, setRefactoredCode] = useState("");
  const [refactorExplanation, setRefactorExplanation] = useState("");

  // Diagnose state
  const [errorLog, setErrorLog] = useState("");
  const [diagnoseContext, setDiagnoseContext] = useState("");
  const [rootCause, setRootCause] = useState("");
  const [suggestedFix, setSuggestedFix] = useState("");
  const [fixedDiff, setFixedDiff] = useState("");

  // Workspace / Local Project Folder state
  const [workspaceConfig, setWorkspaceConfig] = useState<WorkspaceConfig | null>(null);
  const [workspaceFiles, setWorkspaceFiles] = useState<WorkspaceFileItem[]>([]);
  const [isWorkspaceLoading, setIsWorkspaceLoading] = useState(false);
  const [workspaceMessage, setWorkspaceMessage] = useState<string | null>(null);
  const [selectedWorkspaceFile, setSelectedWorkspaceFile] = useState<string | null>(null);
  const [isWorkspaceFilePickerOpen, setIsWorkspaceFilePickerOpen] = useState(false);

  // Auto-scroll reference
  const codeOutputRef = useRef<HTMLPreElement | null>(null);

  // General state
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [snippets, setSnippets] = useState<CodeSnippetItem[]>([]);

  useEffect(() => {
    if (defaultModel) {
      setSelectedModel(defaultModel);
    } else if (models.length > 0) {
      setSelectedModel(models[0].name);
    }
  }, [defaultModel, models]);

  useEffect(() => {
    setSnippets(loadSavedSnippets());
    void refreshWorkspaceConfig();
  }, []);

  // Auto-scroll output block during streaming code generation
  useEffect(() => {
    if (isGenerating && codeOutputRef.current) {
      codeOutputRef.current.scrollTop = codeOutputRef.current.scrollHeight;
    }
  }, [generatedCode, isGenerating]);

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

  async function handleSelectWorkspace(folderPath: string) {
    if (!folderPath.trim()) return;
    setIsWorkspaceLoading(true);
    setWorkspaceMessage(null);
    try {
      const config = await apiRequest<WorkspaceConfig>("/api/workspace/select", {
        method: "POST",
        body: JSON.stringify({ folderPath } as SelectWorkspaceRequest)
      });
      setWorkspaceConfig(config);
      setWorkspaceMessage(`Cartella autorizzata con successo (${config.fileCount} file trovati).`);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceMessage(err instanceof Error ? err.message : "Errore durante l'autorizzazione della cartella.");
    } finally {
      setIsWorkspaceLoading(false);
    }
  }

  async function fetchWorkspaceFiles() {
    setIsWorkspaceLoading(true);
    try {
      const files = await apiRequest<WorkspaceFileItem[]>("/api/workspace/files");
      setWorkspaceFiles(files);
    } catch (err) {
      setWorkspaceMessage(err instanceof Error ? err.message : "Errore durante l'elenco dei file di progetto.");
    } finally {
      setIsWorkspaceLoading(false);
    }
  }

  async function handleLoadWorkspaceFileToContext(relativePath: string) {
    try {
      const res = await apiRequest<ReadWorkspaceFileResponse>("/api/workspace/read-file", {
        method: "POST",
        body: JSON.stringify({ relativePath } as ReadWorkspaceFileRequest)
      });
      setSelectedWorkspaceFile(relativePath);
      if (res.language && res.language !== "text") {
        setSelectedLanguage(res.language);
      }
      if (activeTab === "refactor") {
        setOriginalCode(res.content);
      } else if (activeTab === "diagnose") {
        setDiagnoseContext(res.content);
      } else {
        setCodeContext(res.content);
      }
      setIsWorkspaceFilePickerOpen(false);
      setWorkspaceMessage(`File caricato come contesto: ${relativePath}`);
    } catch (err) {
      setWorkspaceMessage(err instanceof Error ? err.message : "Errore durante la lettura del file dal workspace.");
    }
  }

  async function handleApplyCodeToWorkspace(relativePath: string, contentToApply: string) {
    if (!relativePath.trim() || !contentToApply.trim()) return;
    setIsWorkspaceLoading(true);
    try {
      const res = await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
        method: "POST",
        body: JSON.stringify({ relativePath, content: contentToApply } as WriteWorkspaceFileRequest)
      });
      setWorkspaceMessage(res.message);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceMessage(err instanceof Error ? err.message : "Errore durante il salvataggio nel file di progetto.");
    } finally {
      setIsWorkspaceLoading(false);
    }
  }

  async function handleGenerateCode() {
    if (!prompt.trim()) return;
    setIsGenerating(true);
    setError(null);
    setGeneratedCode("");
    setExplanation("");
    setExecutionSuggestions([
      `Verificare la sintassi e compilare per ${selectedLanguage}.`,
      "Eseguire i test di unità corrispondenti prima del deploy."
    ]);

    try {
      await apiStreamRequest(
        "/api/coding/generate-stream",
        {
          prompt,
          model: selectedModel || null,
          persona: selectedPersona,
          language: selectedLanguage,
          codeContext: codeContext.trim() || null,
          targetFilePath: selectedWorkspaceFile || null
        } as CodingTaskRequest,
        (chunk) => {
          setGeneratedCode((prev) => prev + chunk);
        }
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante la generazione del codice.");
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleRefactorCode() {
    if (!originalCode.trim()) return;
    setIsGenerating(true);
    setError(null);
    try {
      const response = await apiRequest<CodeRefactorResponse>("/api/coding/refactor", {
        method: "POST",
        body: JSON.stringify({
          originalCode,
          goal: refactorGoal,
          model: selectedModel || null,
          language: selectedLanguage
        } as CodeRefactorRequest)
      });

      setRefactoredCode(response.modifiedCode);
      setRefactorExplanation(response.explanation);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante il refactoring.");
    } finally {
      setIsGenerating(false);
    }
  }

  async function handleDiagnoseCode() {
    if (!errorLog.trim()) return;
    setIsGenerating(true);
    setError(null);
    try {
      const response = await apiRequest<CodeDiagnoseResponse>("/api/coding/diagnose", {
        method: "POST",
        body: JSON.stringify({
          errorLog,
          codeContext: diagnoseContext.trim() || null,
          model: selectedModel || null,
          language: selectedLanguage
        } as CodeDiagnoseRequest)
      });

      setRootCause(response.rootCauseAnalysis);
      setSuggestedFix(response.suggestedFixCode);
      setFixedDiff(response.fixedCodeDiff);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Errore durante la diagnosi dell'errore.");
    } finally {
      setIsGenerating(false);
    }
  }

  function handleSaveCurrentSnippet(title: string, code: string, lang: string, exp?: string) {
    if (!code.trim()) return;
    const item = saveSnippet({ title: title || "Snippet senza titolo", code, language: lang, explanation: exp });
    setSnippets((prev) => [item, ...prev]);
  }

  function handleDeleteSnippet(id: string) {
    const updated = deleteSnippet(id);
    setSnippets(updated);
  }

  return {
    activeTab,
    setActiveTab,
    selectedPersona,
    setSelectedPersona,
    selectedModel,
    setSelectedModel,
    selectedLanguage,
    setSelectedLanguage,

    // Workbench
    prompt,
    setPrompt,
    codeContext,
    setCodeContext,
    generatedCode,
    setGeneratedCode,
    explanation,
    setExplanation,
    executionSuggestions,
    handleGenerateCode,
    codeOutputRef,

    // Refactor
    originalCode,
    setOriginalCode,
    refactorGoal,
    setRefactorGoal,
    refactoredCode,
    refactorExplanation,
    handleRefactorCode,

    // Diagnose
    errorLog,
    setErrorLog,
    diagnoseContext,
    setDiagnoseContext,
    rootCause,
    suggestedFix,
    fixedDiff,
    handleDiagnoseCode,

    // Workspace / Project folder
    workspaceConfig,
    workspaceFiles,
    isWorkspaceLoading,
    workspaceMessage,
    selectedWorkspaceFile,
    setSelectedWorkspaceFile,
    isWorkspaceFilePickerOpen,
    setIsWorkspaceFilePickerOpen,
    handleSelectWorkspace,
    handleLoadWorkspaceFileToContext,
    handleApplyCodeToWorkspace,
    refreshWorkspaceConfig,

    // Common
    isGenerating,
    error,
    snippets,
    handleSaveCurrentSnippet,
    handleDeleteSnippet
  };
}
