export const targetLanguages = [
  "English",
  "Spanish",
  "French",
  "German",
  "Italian",
  "Portuguese",
  "Dutch",
  "Polish",
  "Romanian",
  "Chinese",
  "Japanese",
  "Korean",
  "Arabic",
  "Russian"
];

export function formatTranslationStatus(status: string): string {
  switch (status) {
    case "Queued":
      return "In coda";
    case "Running":
      return "In corso";
    case "Completed":
      return "Completata";
    case "Corrected":
      return "Corretta";
    case "Failed":
      return "Errore";
    case "Cancelled":
      return "Annullata";
    case "Pending":
      return "In attesa";
    default:
      return status;
  }
}

export function formatUnitKind(kind: string): string {
  switch (kind) {
    case "table-cell":
      return "Cella";
    case "textbox":
      return "Textbox";
    case "paragraph":
      return "Paragrafo";
    default:
      return "Unità";
  }
}

export function buildCompareDraftKey(translationId: number | null, unitId: number): string {
  return `onlyrag.translation.compare.${translationId ?? "unknown"}.${unitId}`;
}

