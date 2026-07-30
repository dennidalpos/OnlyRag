export function detectLanguageFromFilename(filename: string): string {
  const ext = filename.split(".").pop()?.toLowerCase();
  switch (ext) {
    case "cs":
      return "csharp";
    case "ts":
    case "tsx":
      return "typescript";
    case "js":
    case "jsx":
      return "javascript";
    case "py":
      return "python";
    case "html":
      return "html";
    case "css":
      return "css";
    case "json":
      return "json";
    case "sql":
      return "sql";
    case "ps1":
      return "powershell";
    default:
      return "csharp";
  }
}

export function formatWorkspaceTreeSummary(rootPath: string | null, files: { relativePath: string; isDirectory: boolean; sizeBytes: number }[]): string {
  if (!rootPath || files.length === 0) {
    return "Nessuna struttura file disponibile.";
  }

  const fileList = files
    .slice(0, 100)
    .map((f) => `- ${f.relativePath}${f.isDirectory ? "/" : ` (${(f.sizeBytes / 1024).toFixed(1)} KB)`}`)
    .join("\n");

  return `Cartella Progetto: ${rootPath}\nTotale File Indicizzati: ${files.length}\nStruttura File:\n${fileList}`;
}

export type DiffLine = {
  type: "add" | "delete" | "normal";
  oldLineNumber?: number;
  newLineNumber?: number;
  content: string;
};

export function computeLineDiff(oldText: string, newText: string): DiffLine[] {
  const oldLines = oldText ? oldText.split("\n") : [];
  const newLines = newText ? newText.split("\n") : [];

  if (oldLines.length === 0) {
    return newLines.map((line, idx) => ({
      type: "add",
      newLineNumber: idx + 1,
      content: line
    }));
  }

  const result: DiffLine[] = [];
  let i = 0;
  let j = 0;

  while (i < oldLines.length || j < newLines.length) {
    if (i < oldLines.length && j < newLines.length && oldLines[i] === newLines[j]) {
      result.push({
        type: "normal",
        oldLineNumber: i + 1,
        newLineNumber: j + 1,
        content: oldLines[i]
      });
      i++;
      j++;
    } else {
      let findInNew = -1;
      for (let lookAhead = j + 1; lookAhead < Math.min(newLines.length, j + 10); lookAhead++) {
        if (i < oldLines.length && oldLines[i] === newLines[lookAhead]) {
          findInNew = lookAhead;
          break;
        }
      }

      if (findInNew !== -1) {
        while (j < findInNew) {
          result.push({
            type: "add",
            newLineNumber: j + 1,
            content: newLines[j]
          });
          j++;
        }
      } else {
        if (i < oldLines.length) {
          result.push({
            type: "delete",
            oldLineNumber: i + 1,
            content: oldLines[i]
          });
          i++;
        }
        if (j < newLines.length && (i >= oldLines.length || oldLines[i] !== newLines[j])) {
          result.push({
            type: "add",
            newLineNumber: j + 1,
            content: newLines[j]
          });
          j++;
        }
      }
    }
  }

  return result;
}
