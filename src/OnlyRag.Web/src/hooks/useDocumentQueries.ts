import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  apiRequest,
  type ImportedDocument,
  type OcrLanguage,
  type OcrProcessingSettings,
  type VectorBackendHealth
} from "../api";
import { fallbackOcrLanguages } from "../components/DocumentsSection.controllerHelpers";

export function useDocumentListQuery() {
  return useQuery<ImportedDocument[], Error>({
    queryKey: ["documentsList"],
    queryFn: async () => {
      return await apiRequest<ImportedDocument[]>("/api/documents");
    },
    refetchInterval: 5000,
    retry: 1
  });
}

export function useVectorHealthQuery() {
  return useQuery<VectorBackendHealth, Error>({
    queryKey: ["vectorHealth"],
    queryFn: async () => {
      return await apiRequest<VectorBackendHealth>("/api/diagnostics/vector-health");
    },
    refetchInterval: 10000,
    retry: 1
  });
}

export function useOcrLanguagesQuery() {
  return useQuery<OcrLanguage[], Error>({
    queryKey: ["ocrLanguages"],
    queryFn: async () => {
      const languages = await apiRequest<OcrLanguage[]>("/api/ocr/languages");
      return languages.length > 0 ? languages : fallbackOcrLanguages;
    },
    staleTime: 60000
  });
}

export function useOcrSettingsQuery() {
  return useQuery<OcrProcessingSettings, Error>({
    queryKey: ["ocrSettings"],
    queryFn: async () => {
      return await apiRequest<OcrProcessingSettings>("/api/settings/ocr-processing");
    },
    staleTime: 30000
  });
}

export function useInvalidateDocuments() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ["documentsList"] });
    void queryClient.invalidateQueries({ queryKey: ["vectorHealth"] });
  };
}
