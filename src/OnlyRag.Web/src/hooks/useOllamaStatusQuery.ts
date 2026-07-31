import { useQuery } from "@tanstack/react-query";
import {
  apiRequest,
  type OllamaInstallStatus,
  type OllamaModel,
  type OllamaModelsResponse,
  type OllamaSettings,
  type OllamaStatusResponse
} from "../api";

export type OllamaStateData = {
  settings: OllamaSettings | null;
  status: OllamaStatusResponse | null;
  installStatus: OllamaInstallStatus | null;
  models: OllamaModel[];
  loadError: string | null;
};

export function useOllamaStatusQuery() {
  const settingsQuery = useQuery<OllamaSettings>({
    queryKey: ["ollamaSettings"],
    queryFn: () => apiRequest<OllamaSettings>("/api/settings/ollama"),
    refetchInterval: 5000,
    retry: 1
  });

  const statusQuery = useQuery<OllamaStatusResponse>({
    queryKey: ["ollamaStatus"],
    queryFn: () => apiRequest<OllamaStatusResponse>("/api/ollama/status"),
    refetchInterval: 5000,
    retry: 1
  });

  const installStatusQuery = useQuery<OllamaInstallStatus | null>({
    queryKey: ["ollamaInstallStatus"],
    queryFn: () => apiRequest<OllamaInstallStatus>("/api/dependencies/ollama").catch(() => null),
    refetchInterval: 5000,
    retry: 1
  });

  const isReachable = statusQuery.data?.isReachable ?? false;

  const modelsQuery = useQuery<OllamaModelsResponse>({
    queryKey: ["ollamaModels"],
    queryFn: () => apiRequest<OllamaModelsResponse>("/api/ollama/models"),
    enabled: isReachable,
    refetchInterval: 5000,
    retry: 1
  });

  const settings = settingsQuery.data ?? null;
  const status = statusQuery.data ?? null;
  const installStatus = installStatusQuery.data ?? null;
  const models = modelsQuery.data?.models ?? [];
  const loadError = isReachable
    ? modelsQuery.error
      ? modelsQuery.error.message
      : null
    : status?.suggestion ?? status?.message ?? null;

  return {
    data: {
      settings,
      status,
      installStatus,
      models,
      loadError
    },
    isLoading: settingsQuery.isLoading || statusQuery.isLoading,
    isFetched: (settingsQuery.isFetched || settingsQuery.data !== undefined) && (statusQuery.isFetched || statusQuery.data !== undefined),
    refetch: async () => {
      await Promise.all([
        settingsQuery.refetch(),
        statusQuery.refetch(),
        installStatusQuery.refetch(),
        modelsQuery.refetch()
      ]);
    }
  };
}
