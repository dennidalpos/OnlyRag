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

