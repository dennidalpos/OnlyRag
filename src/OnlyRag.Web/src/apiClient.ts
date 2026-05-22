export type BackendBridge = {
  isRunning: boolean;
  baseUrl: string | null;
  apiToken: string | null;
  apiTokenHeaderName: string;
  errorMessage: string | null;
};

export type ApiProblemDetails = {
  title?: string;
  detail?: string;
  status?: number;
  code?: string;
  traceId?: string;
};

declare global {
  interface Window {
    __ONLYRAG_BACKEND__?: BackendBridge;
  }
}

export function resolveBackendBaseUrl(): string | null {
  const bridge = window.__ONLYRAG_BACKEND__;
  return bridge?.isRunning && bridge.baseUrl ? bridge.baseUrl : null;
}

export function resolveBackendBaseUrlDirect(): string | null {
  return window.__ONLYRAG_BACKEND__?.baseUrl ?? null;
}

export function resolveBackendErrorMessage(): string | null {
  const message = window.__ONLYRAG_BACKEND__?.errorMessage;
  return message && message.trim().length > 0 ? message : null;
}

export function resolveBackendSessionToken(): { headerName: string; token: string } | null {
  const bridge = window.__ONLYRAG_BACKEND__;
  if (!bridge?.apiToken || !bridge.apiTokenHeaderName) {
    return null;
  }

  return {
    headerName: bridge.apiTokenHeaderName,
    token: bridge.apiToken
  };
}

export function markBackendOnline(): void {
  const bridge = window.__ONLYRAG_BACKEND__;
  if (bridge) {
    bridge.isRunning = true;
  }
}

export function markBackendOffline(): void {
  const bridge = window.__ONLYRAG_BACKEND__;
  if (bridge) {
    bridge.isRunning = false;
  }
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const baseUrl = resolveBackendBaseUrl();
  if (!baseUrl) {
    throw new Error(resolveBackendErrorMessage() ?? "Il backend locale non è disponibile. Riavviare l'applicazione.");
  }

  const requestUrl = resolveBackendRequestUrl(path, baseUrl);

  const headers = new Headers(init?.headers);
  if (!(init?.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const sessionToken = resolveBackendSessionToken();
  if (!sessionToken) {
    throw new Error("Il token di sessione del backend locale non è disponibile. Riavviare l'applicazione.");
  }

  headers.set(sessionToken.headerName, sessionToken.token);

  let response: Response;
  try {
    response = await fetch(requestUrl, { ...init, headers });
  } catch {
    markBackendOffline();
    throw new Error("Il backend locale non è raggiungibile. Riavviare l'applicazione.");
  }

  if (!response.ok) {
    throw new Error(await readProblemMessage(response));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

function resolveBackendRequestUrl(path: string, baseUrl: string): URL {
  const url = new URL(path, baseUrl);
  const backendOrigin = new URL(baseUrl).origin;
  if (url.origin !== backendOrigin || !url.pathname.startsWith("/api/")) {
    throw new Error("Percorso API locale non valido.");
  }

  return url;
}

async function readProblemMessage(response: Response): Promise<string> {
  try {
    const payload = (await response.json()) as ApiProblemDetails;

    return payload.detail ?? payload.title ?? `Richiesta fallita con stato ${response.status}.`;
  } catch {
    return `Richiesta fallita con stato ${response.status}.`;
  }
}
