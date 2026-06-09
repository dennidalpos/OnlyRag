import type {
  IngestionSettings,
  PdfExportSettings,
  OcrProcessingSettings,
  OcrSettings,
  OllamaSettings,
  PerformanceSettings
} from "../api";
import {
  areIngestionSettingsEqual,
  arePdfExportSettingsEqual,
  areOcrProcessingSettingsEqual,
  areOcrSettingsEqual,
  areOllamaSettingsEqual,
  arePerformanceSettingsEqual
} from "./SettingsSection.helpers";

type SettingsDirtyStateParams = {
  formState: OllamaSettings;
  savedFormState: OllamaSettings;
  pdfExportFormState: PdfExportSettings;
  savedPdfExportFormState: PdfExportSettings;
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
  pdfExportFormState,
  savedPdfExportFormState,
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
  const hasDirtyPdfExportSettings = !arePdfExportSettingsEqual(pdfExportFormState, savedPdfExportFormState);
  const hasDirtyPerformanceSettings = !arePerformanceSettingsEqual(performanceFormState, savedPerformanceFormState);
  const hasDirtyIngestionSettings = !areIngestionSettingsEqual(ingestionFormState, savedIngestionFormState);
  const hasDirtyOcrProcessingSettings = !areOcrProcessingSettingsEqual(
    ocrProcessingFormState,
    savedOcrProcessingFormState
  );
  const hasDirtyOcrSettings = !areOcrSettingsEqual(ocrFormState, savedOcrFormState);
  const hasPendingChanges =
    hasDirtyOllamaSettings ||
    hasDirtyPdfExportSettings ||
    hasDirtyPerformanceSettings ||
    hasDirtyIngestionSettings ||
    hasDirtyOcrProcessingSettings ||
    hasDirtyOcrSettings;

  return {
    hasDirtyOllamaSettings,
    hasDirtyPdfExportSettings,
    hasDirtyPerformanceSettings,
    hasDirtyIngestionSettings,
    hasDirtyOcrProcessingSettings,
    hasDirtyOcrSettings,
    hasPendingChanges
  } as const;
}
