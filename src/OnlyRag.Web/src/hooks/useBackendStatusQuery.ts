import { useQuery } from "@tanstack/react-query";
import {
  apiRequest,
  markBackendOffline,
  markBackendOnline
} from "../api";
import {
  initialRefreshStatus,
  type RefreshStatus
} from "../pollingStatus";

export type AppStatusResponse = {
  backend: string;
  database: string;
  jobQueue: string;
  ollama: string;
  startedAtUtc: string;
  lowResourceMode: boolean;
};

export type StatusTone = "online" | "offline" | "warning";

export type BackendStatus = {
  backendValue: string;
  backendTone: StatusTone;
  ollamaValue: string;
  ollamaTone: StatusTone;
  jobsValue: string;
  jobsTone: StatusTone;
  lowResourceMode: boolean;
  refreshStatus: RefreshStatus;
};

export const offlineBackendStatus: BackendStatus = {
  backendValue: "Offline",
  backendTone: "offline",
  ollamaValue: "Offline",
  ollamaTone: "offline",
  jobsValue: "0",
  jobsTone: "offline",
  lowResourceMode: false,
  refreshStatus: initialRefreshStatus
};

export function useBackendStatusQuery() {
  return useQuery<AppStatusResponse, Error>({
    queryKey: ["backendStatus"],
    queryFn: async () => {
      try {
        const data = await apiRequest<AppStatusResponse>("/api/app/status");
        markBackendOnline();
        return data;
      } catch (err) {
        markBackendOffline();
        throw err;
      }
    },
    refetchInterval: 3000,
    retry: 1
  });
}
