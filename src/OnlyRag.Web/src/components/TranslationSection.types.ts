export type FeedbackState = {
  tone: "info" | "error";
  message: string;
} | null;

export type ExportFormat = "txt" | "markdown" | "html" | "docx" | "pdf";
