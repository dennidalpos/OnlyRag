export const targetLanguageOptions = [
  { value: "English", label: "Inglese" },
  { value: "Spanish", label: "Spagnolo" },
  { value: "French", label: "Francese" },
  { value: "German", label: "Tedesco" },
  { value: "Italian", label: "Italiano" },
  { value: "Portuguese", label: "Portoghese" },
  { value: "Dutch", label: "Olandese" },
  { value: "Polish", label: "Polacco" },
  { value: "Romanian", label: "Rumeno" },
  { value: "Chinese", label: "Cinese" },
  { value: "Japanese", label: "Giapponese" },
  { value: "Korean", label: "Coreano" },
  { value: "Arabic", label: "Arabo" },
  { value: "Russian", label: "Russo" }
] as const;

export function formatTargetLanguageLabel(language: string): string {
  return targetLanguageOptions.find((option) => option.value === language)?.label ?? language;
}

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

