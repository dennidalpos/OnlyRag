import type { ImportedDocument, OcrLanguage } from "../api";

const DEFAULT_OCR_LANGUAGE = "it";

export const statusLabels: Record<string, string> = {
  Imported: "Importato",
  Queued: "In coda",
  Processing: "In lavorazione",
  Indexed: "Pronto",
  RequiresAdditionalComponent: "Richiede componente",
  Failed: "Errore"
};

export function getDocumentStatusClassName(status: string): string {
  return `job-status job-status--${status.toLowerCase()}`;
}

export function isOcrCandidate(document: ImportedDocument): boolean {
  const ext = document.fileExtension?.toLowerCase() ?? "";
  return [".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff"].includes(ext);
}

export function getDefaultOcrLanguage(languages: OcrLanguage[]): string {
  return languages.find((language) => language.isDefault)?.code
    ?? languages[0]?.code
    ?? DEFAULT_OCR_LANGUAGE;
}

export function getPreferredOcrLanguage(languages: OcrLanguage[], preferredLanguage: string | null): string {
  const normalized = preferredLanguage?.trim() ?? "";
  if (normalized.length > 0 && languages.some((language) => language.code === normalized)) {
    return normalized;
  }

  return getDefaultOcrLanguage(languages);
}

export function formatDocumentType(document: ImportedDocument): string {
  switch (document.fileExtension?.toLowerCase()) {
    case ".docx":
      return "Word Open XML";
    case ".doc":
      return "Word legacy";
    case ".xlsx":
      return "Excel Open XML";
    case ".xls":
      return "Excel legacy";
    case ".pptx":
      return "PowerPoint Open XML";
    case ".ppt":
      return "PowerPoint legacy";
    case ".pdf":
      return "PDF";
    case ".md":
    case ".markdown":
      return "Markdown";
    case ".txt":
      return "Testo";
    default:
      return document.mimeType ?? document.fileExtension ?? "Sconosciuto";
  }
}

export function formatIndexedUnitLabel(document: ImportedDocument): string {
  switch (document.fileExtension?.toLowerCase()) {
    case ".docx":
      return "Sezioni logiche";
    case ".xlsx":
      return "Fogli indicizzati";
    case ".pptx":
      return "Slide indicizzate";
    default:
      return "Pagine";
  }
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}
