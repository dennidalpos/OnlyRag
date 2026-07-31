import { useQuery } from "@tanstack/react-query";
import { apiRequest, type DiagnosticsResponse } from "../api";

export function useDiagnosticsQuery() {
  return useQuery<DiagnosticsResponse, Error>({
    queryKey: ["diagnostics"],
    queryFn: async () => {
      return await apiRequest<DiagnosticsResponse>("/api/diagnostics");
    },
    refetchInterval: 10000,
    retry: 1
  });
}
