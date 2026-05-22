import type { OllamaModel, TranslationCompare } from "../api";

export function getPreferredTranslationModel(
  models: OllamaModel[],
  defaultModel: string | null
): string {
  const modelNames = models.map((model) => model.name);
  return defaultModel && modelNames.includes(defaultModel) ? defaultModel : modelNames[0] ?? "";
}

export function getFirstAvailableCompareUnit(data: TranslationCompare) {
  return data.units.find((unit) => unit.translatedText || unit.machineTranslatedText)
    ?? data.units[0]
    ?? null;
}

export function loadCompareDraft(compareDraftKey: string | null): string | null {
  try {
    return compareDraftKey ? window.localStorage.getItem(compareDraftKey) : null;
  } catch {
    return null;
  }
}

export function saveOrClearCompareDraft(
  compareDraftKey: string | null,
  hasUnsavedCompareDraft: boolean,
  editedTranslationText: string
): void {
  if (!compareDraftKey) {
    return;
  }

  try {
    if (!hasUnsavedCompareDraft) {
      window.localStorage.removeItem(compareDraftKey);
    } else {
      window.localStorage.setItem(compareDraftKey, editedTranslationText);
    }
  } catch {
  }
}
