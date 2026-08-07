import { useEffect, useState } from "react";
import { apiRequest } from "../../api";
import type {
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
} from "../../apiTypes";

export type SingleAnalysisFile = {
  id: string;
  name: string;
  relativePath: string;
  sizeBytes: number;
  content: string;
};

export function useWorkspaceManager() {
  const [workspaceConfig, setWorkspaceConfig] = useState<WorkspaceConfig | null>(null);
  const [workspaceFiles, setWorkspaceFiles] = useState<WorkspaceFileItem[]>([]);
  const [selectedWorkspaceFile, setSelectedWorkspaceFile] = useState<string | null>(null);
  const [attachedFileContent, setAttachedFileContent] = useState<string | null>(null);
  const [singleFiles, setSingleFiles] = useState<SingleAnalysisFile[]>([]);
  const [isWorkspaceFilePickerOpen, setIsWorkspaceFilePickerOpen] = useState(false);
  const [isAttachedFileEditorOpen, setIsAttachedFileEditorOpen] = useState(false);
  const [workspaceStatusMessage, setWorkspaceStatusMessage] = useState<string | null>(null);

  // Diff Modal State
  const [diffModalFile, setDiffModalFile] = useState<string | null>(null);
  const [diffModalOriginalContent, setDiffModalOriginalContent] = useState<string>("");
  const [diffModalModifiedContent, setDiffModalModifiedContent] = useState<string>("");
  const [diffModalFileApplied, setDiffModalFileApplied] = useState<boolean>(false);

  useEffect(() => {
    void refreshWorkspaceConfig();
  }, []);

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
      // Backend non pronto
    }
  }

  async function fetchWorkspaceFiles() {
    try {
      const files = await apiRequest<WorkspaceFileItem[]>("/api/workspace/files");
      setWorkspaceFiles(files);
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'elenco dei file di progetto."
      );
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
        setWorkspaceStatusMessage(
          `Cartella di progetto impostata: ${config.rootPath} (${config.fileCount} file)`
        );
        void fetchWorkspaceFiles();
      } else {
        setWorkspaceStatusMessage(null);
      }
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Impossibile aprire la selezione cartella."
      );
    }
  }

  async function handleClearWorkspaceFolder() {
    try {
      const config = await apiRequest<WorkspaceConfig>("/api/workspace/clear", {
        method: "POST"
      });
      setWorkspaceConfig(config);
      setWorkspaceFiles([]);
      setSelectedWorkspaceFile(null);
      setAttachedFileContent(null);
      setWorkspaceStatusMessage("Cartella di progetto rimossa.");
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante la rimozione della cartella."
      );
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
          body: JSON.stringify({
            relativePath: selectedWorkspaceFile,
            content: updatedContent
          } as WriteWorkspaceFileRequest)
        });
        setWorkspaceStatusMessage(res.message);
        void fetchWorkspaceFiles();
      } catch (err) {
        setWorkspaceStatusMessage(
          err instanceof Error ? err.message : "Errore durante il salvataggio su disco."
        );
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
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Impossibile aprire il file esternamente."
      );
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

  async function handleOpenDiff(relativePath: string, modifiedCode?: string, isApplied?: boolean) {
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
      setDiffModalFileApplied(Boolean(isApplied));
    } catch {
      setDiffModalFile(relativePath);
      setDiffModalOriginalContent("");
      setDiffModalModifiedContent(modifiedCode ?? "");
      setDiffModalFileApplied(Boolean(isApplied));
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
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante il salvataggio su disco."
      );
    }
  }

  async function handleRollbackFileContent(relativePath: string, originalContent: string) {
    if (!relativePath || !originalContent) return;
    try {
      const res = await apiRequest<WriteWorkspaceFileResponse>("/api/workspace/write-file", {
        method: "POST",
        body: JSON.stringify({ relativePath, content: originalContent } as WriteWorkspaceFileRequest)
      });
      setWorkspaceStatusMessage(res.message || `⏪ Ripristinata la versione originale di ${relativePath}`);
      void fetchWorkspaceFiles();
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante il ripristino del file."
      );
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
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'eliminazione del file."
      );
    }
  }

  async function handleExecuteWorkspaceCommand(cmdToRun?: string, onOutput?: (msg: string) => void) {
    const cmd = cmdToRun || "dotnet build";
    setWorkspaceStatusMessage(`Esecuzione comando nel workspace: ${cmd}...`);
    try {
      const res = await apiRequest<ExecuteWorkspaceCommandResponse>(
        "/api/workspace/execute-command",
        {
          method: "POST",
          body: JSON.stringify({ command: cmd } as ExecuteWorkspaceCommandRequest)
        }
      );

      const logMsg = `💻 **Esecuzione Comando**: \`${cmd}\`\n\n${
        res.success
          ? "✅ Esecuzione completata con successo (Exit 0)"
          : `❌ Esecuzione terminata con errore (Exit ${res.exitCode})`
      }\n\n\`\`\`text\n${res.output || res.error || "Nessun output restituito dal processo."}\n\`\`\``;

      if (onOutput) onOutput(logMsg);

      setWorkspaceStatusMessage(
        res.success
          ? `Comando eseguito con successo (${cmd})`
          : `Comando completato con errore (${cmd})`
      );
    } catch (err) {
      setWorkspaceStatusMessage(
        err instanceof Error ? err.message : "Errore durante l'esecuzione del comando."
      );
    }
  }

  async function handleAddSingleFiles() {
    try {
      if ("showOpenFilePicker" in window) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const fileHandles = await (window as any).showOpenFilePicker({
          multiple: true
        });
        const newFiles: SingleAnalysisFile[] = await Promise.all(
          // eslint-disable-next-line @typescript-eslint/no-explicit-any
          fileHandles.map(async (handle: any) => {
            const file: File = await handle.getFile();
            const content = await file.text();
            return {
              id: `single_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
              name: file.name,
              relativePath: file.name,
              sizeBytes: file.size,
              content
            };
          })
        );
        setSingleFiles((prev) => [...prev, ...newFiles]);
        setWorkspaceStatusMessage(`${newFiles.length} file aggiunto/i per l'analisi ad-hoc.`);
      } else {
        const input = document.createElement("input");
        input.type = "file";
        input.multiple = true;
        input.onchange = async () => {
          if (!input.files || input.files.length === 0) return;
          const filesArray = Array.from(input.files);
          const loaded: SingleAnalysisFile[] = await Promise.all(
            filesArray.map(async (file) => ({
              id: `single_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
              name: file.name,
              relativePath: file.name,
              sizeBytes: file.size,
              content: await file.text()
            }))
          );
          setSingleFiles((prev) => [...prev, ...loaded]);
          setWorkspaceStatusMessage(`${loaded.length} file aggiunto/i per l'analisi ad-hoc.`);
        };
        input.click();
      }
    } catch {
      // Dialogo annullato dall'utente
    }
  }

  async function handleImportFileList(files: FileList | File[]) {
    if (!files || files.length === 0) return;
    const filesArray = Array.from(files);
    const loaded: SingleAnalysisFile[] = await Promise.all(
      filesArray.map(async (file) => ({
        id: `single_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`,
        name: file.name,
        relativePath: file.name,
        sizeBytes: file.size,
        content: await file.text()
      }))
    );
    setSingleFiles((prev) => [...prev, ...loaded]);
    setWorkspaceStatusMessage(`${loaded.length} file inserito/i nella pipeline Coding.`);
  }

  function handleRemoveSingleFile(id: string) {
    setSingleFiles((prev) => prev.filter((f) => f.id !== id));
  }

  function handleClearSingleFiles() {
    setSingleFiles([]);
  }

  return {
    workspaceConfig,
    workspaceFiles,
    selectedWorkspaceFile,
    setSelectedWorkspaceFile,
    attachedFileContent,
    setAttachedFileContent,
    singleFiles,
    setSingleFiles,
    isWorkspaceFilePickerOpen,
    setIsWorkspaceFilePickerOpen,
    isAttachedFileEditorOpen,
    setIsAttachedFileEditorOpen,
    workspaceStatusMessage,
    setWorkspaceStatusMessage,
    diffModalFile,
    setDiffModalFile,
    diffModalOriginalContent,
    diffModalModifiedContent,
    diffModalFileApplied,
    fetchWorkspaceFiles,
    handlePickWindowsFolder,
    handleClearWorkspaceFolder,
    handleAttachWorkspaceFile,
    handleSaveAttachedFileContent,
    handleOpenExternalFile,
    handleApplyCodeToFileSilently,
    handleDeleteWorkspaceFileSilently,
    handleOpenDiff,
    handleApplyCodeToFile,
    handleRollbackFileContent,
    handleDeleteWorkspaceFile,
    handleExecuteWorkspaceCommand,
    handleAddSingleFiles,
    handleImportFileList,
    handleRemoveSingleFile,
    handleClearSingleFiles
  };
}
