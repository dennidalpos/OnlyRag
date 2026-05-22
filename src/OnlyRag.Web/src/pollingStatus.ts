export type RefreshStatus = {
  lastSuccessfulRefreshAt: string | null;
  consecutiveFailureCount: number;
  lastErrorMessage: string | null;
};

export const initialRefreshStatus: RefreshStatus = {
  lastSuccessfulRefreshAt: null,
  consecutiveFailureCount: 0,
  lastErrorMessage: null
};

export function markRefreshSuccess(now: Date = new Date()): RefreshStatus {
  return {
    lastSuccessfulRefreshAt: now.toISOString(),
    consecutiveFailureCount: 0,
    lastErrorMessage: null
  };
}

export function markRefreshFailure(current: RefreshStatus, message: string): RefreshStatus {
  return {
    ...current,
    consecutiveFailureCount: current.consecutiveFailureCount + 1,
    lastErrorMessage: message
  };
}

export function shouldSurfaceRefreshFailure(status: RefreshStatus): boolean {
  return status.consecutiveFailureCount >= 2;
}

const displayLocale = "it-IT";

export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString(displayLocale);
}

export function formatTime(value: string): string {
  return new Date(value).toLocaleTimeString(displayLocale);
}

export function formatLastRefresh(value: string | null): string {
  if (!value) {
    return "Mai aggiornato";
  }

  return formatDateTime(value);
}
