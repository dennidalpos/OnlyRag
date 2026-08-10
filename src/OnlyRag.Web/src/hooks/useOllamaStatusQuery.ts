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
    staleTime: 60000,
    refetchOnWindowFocus: false,
    retry: 1
  });


  // Ollama is an external process — poll infrequently as a liveness fallback
  const statusQuery = useQuery<OllamaStatusResponse>({
    queryKey: ["ollamaStatus"],
    queryFn: () => apiRequest<OllamaStatusResponse>("/api/ollama/status"),
    refetchInterval: 60000,
    refetchOnWindowFocus: false,
    retry: 1
  });


  const installStatusQuery = useQuery<OllamaInstallStatus | null>({
    queryKey: ["ollamaInstallStatus"],
    queryFn: () => apiRequest<OllamaInstallStatus>("/api/dependencies/ollama").catch(() => null),
    staleTime: 120000,
    refetchOnWindowFocus: false,
    retry: 1
  });


  const isReachable = statusQuery.data?.isReachable ?? false;

  const modelsQuery = useQuery<OllamaModelsResponse>({
    queryKey: ["ollamaModels"],
    queryFn: () => apiRequest<OllamaModelsResponse>("/api/ollama/models"),
    enabled: isReachable,
    staleTime: 60000,
    refetchOnWindowFocus: false,
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
      const [statusResult] = await Promise.all([
        statusQuery.refetch(),
        settingsQuery.refetch(),
        installStatusQuery.refetch()
      ]);
      if (statusResult.data?.isReachable) {
        await modelsQuery.refetch();
      }
    }
  };
}
