import type {
  IngestionSettings,
  OfficeConversionSettings,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceSettings
} from "../api";
import {
  areIngestionSettingsEqual,
  areOfficeSettingsEqual,
  areOcrProcessingSettingsEqual,
  areOcrSettingsEqual,
  areOllamaSettingsEqual,
  arePerformanceSettingsEqual
} from "./SettingsSection.helpers";

type SettingsDirtyStateParams = {
  formState: OllamaSettings;
  savedFormState: OllamaSettings;
  officeFormState: OfficeConversionSettings;
  savedOfficeFormState: OfficeConversionSettings;
  performanceFormState: PerformanceSettings;
  savedPerformanceFormState: PerformanceSettings;
  ingestionFormState: IngestionSettings;
  savedIngestionFormState: IngestionSettings;
  ocrProcessingFormState: OcrProcessingSettings;
  savedOcrProcessingFormState: OcrProcessingSettings;
  ocrFormState: OcrSettings;
  savedOcrFormState: OcrSettings;
};

export function useSettingsDirtyState({
  formState,
  savedFormState,
  officeFormState,
  savedOfficeFormState,
  performanceFormState,
  savedPerformanceFormState,
  ingestionFormState,
  savedIngestionFormState,
  ocrProcessingFormState,
  savedOcrProcessingFormState,
  ocrFormState,
  savedOcrFormState
}: SettingsDirtyStateParams) {
  const hasDirtyOllamaSettings = !areOllamaSettingsEqual(formState, savedFormState);
  const hasDirtyOfficeSettings = !areOfficeSettingsEqual(officeFormState, savedOfficeFormState);
  const hasDirtyPerformanceSettings = !arePerformanceSettingsEqual(performanceFormState, savedPerformanceFormState);
  const hasDirtyIngestionSettings = !areIngestionSettingsEqual(ingestionFormState, savedIngestionFormState);
  const hasDirtyOcrProcessingSettings = !areOcrProcessingSettingsEqual(
    ocrProcessingFormState,
    savedOcrProcessingFormState
  );
  const hasDirtyOcrSettings = !areOcrSettingsEqual(ocrFormState, savedOcrFormState);
  const hasPendingChanges =
    hasDirtyOllamaSettings ||
    hasDirtyOfficeSettings ||
    hasDirtyPerformanceSettings ||
    hasDirtyIngestionSettings ||
    hasDirtyOcrProcessingSettings ||
    hasDirtyOcrSettings;

  return {
    hasDirtyOllamaSettings,
    hasDirtyOfficeSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges
  } as const;
}
