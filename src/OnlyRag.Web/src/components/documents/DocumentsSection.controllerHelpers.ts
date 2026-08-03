import { apiRequest, type ImportedDocument, type LocalJob, type OcrLanguage } from "../../api";

export const DEFAULT_OCR_LANGUAGE = "it";

export const fallbackOcrLanguages: OcrLanguage[] = [
  { code: "it", label: "Italiano", scriptGroup: "Principali", isDefault: true }
];

const OCR_EXTENSIONS = new Set([".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp", ".gif", ".webp"]);

export type FeedbackTone = "info" | "error";
export type FeedbackState = { tone: FeedbackTone; message: string } | null;
export type PendingImport = { files: FileList | File[] };
export type PendingOcrAction =
  | { kind: "reindex"; document: ImportedDocument }
  | { kind: "ocr"; document: ImportedDocument; force: boolean };

export function anyOcrCandidate(files: FileList | File[]): boolean {
  for (const file of Array.from(files)) {
    const ext = ("." + file.name.split(".").pop()!).toLowerCase();
    if (OCR_EXTENSIONS.has(ext)) return true;
  }
  return false;
}

export async function hasBlockingDocumentJob(document: ImportedDocument): Promise<boolean> {
  if (!document.currentJobId) {
    return false;
  }

  const job = await apiRequest<LocalJob>(`/api/jobs/${document.currentJobId}`).catch(() => null);
  return job?.status === "Running" || job?.status === "Pausing" || job?.status === "Pending";
}
