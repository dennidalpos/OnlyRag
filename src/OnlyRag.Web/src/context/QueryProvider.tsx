import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";

export function createQueryClient(): QueryClient {
  const isTest = typeof process !== "undefined" && (process.env.NODE_ENV === "test" || process.env.VITEST === "true");
  return new QueryClient({
    defaultOptions: {
      queries: {
        refetchOnWindowFocus: false,
        retry: isTest ? false : 1,
        staleTime: isTest ? 0 : 2000,
        gcTime: isTest ? 0 : 5 * 60 * 1000
      }
    }
  });
}

type QueryProviderProps = {
  children: ReactNode;
  client?: QueryClient;
};

export function QueryProvider({ children, client }: QueryProviderProps) {
  const [queryClient] = useState(() => client ?? createQueryClient());

  return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>;
}
