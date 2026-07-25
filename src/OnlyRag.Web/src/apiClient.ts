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

export type ApiRequestOptions = RequestInit & {
  retries?: number;
  retryDelayMs?: number;
  retryOnStatusCodes?: number[];
};

declare global {
  interface Window {
    __ONLYRAG_BACKEND__?: BackendBridge;
  }
}

const DEFAULT_RETRIES = 2;
const DEFAULT_RETRY_DELAY_MS = 150;
const DEFAULT_RETRY_STATUS_CODES = [404, 408, 500, 502, 503, 504];

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
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

export async function apiRequest<T>(path: string, options?: ApiRequestOptions): Promise<T> {
  const {
    retries = DEFAULT_RETRIES,
    retryDelayMs = DEFAULT_RETRY_DELAY_MS,
    retryOnStatusCodes = DEFAULT_RETRY_STATUS_CODES,
    ...init
  } = options ?? {};

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

  let attempt = 0;
  while (attempt <= retries) {
    let response: Response;
    try {
      response = await fetch(requestUrl, { ...init, headers });
    } catch {
      if (attempt < retries) {
        attempt++;
        await delay(retryDelayMs * Math.pow(2, attempt - 1));
        continue;
      }
      markBackendOffline();
      throw new Error("Il backend locale non è raggiungibile. Riavviare l'applicazione.");
    }

    if (response.ok) {
      if (response.status === 204) {
        return undefined as T;
      }
      return (await response.json()) as T;
    }

    if (attempt < retries && retryOnStatusCodes.includes(response.status)) {
      attempt++;
      await delay(retryDelayMs * Math.pow(2, attempt - 1));
      continue;
    }

    throw new Error(await readProblemMessage(response, path));
  }

  throw new Error("Richiesta fallita dopo diversi tentativi.");
}

export async function apiStreamRequest(
  path: string,
  body: unknown,
  onChunk: (chunk: string) => void,
  signal?: AbortSignal,
  options?: ApiRequestOptions
): Promise<void> {
  const {
    retries = DEFAULT_RETRIES,
    retryDelayMs = DEFAULT_RETRY_DELAY_MS,
    retryOnStatusCodes = DEFAULT_RETRY_STATUS_CODES
  } = options ?? {};

  const baseUrl = resolveBackendBaseUrl();
  if (!baseUrl) {
    throw new Error(resolveBackendErrorMessage() ?? "Il backend locale non è disponibile. Riavviare l'applicazione.");
  }

  const requestUrl = resolveBackendRequestUrl(path, baseUrl);
  const headers = new Headers();
  headers.set("Content-Type", "application/json");

  const sessionToken = resolveBackendSessionToken();
  if (!sessionToken) {
    throw new Error("Il token di sessione del backend locale non è disponibile. Riavviare l'applicazione.");
  }
  headers.set(sessionToken.headerName, sessionToken.token);

  let response: Response | undefined;
  let attempt = 0;

  while (attempt <= retries) {
    try {
      response = await fetch(requestUrl, {
        method: "POST",
        headers,
        body: JSON.stringify(body),
        signal
      });
    } catch (e) {
      if (attempt < retries && !(signal?.aborted)) {
        attempt++;
        await delay(retryDelayMs * Math.pow(2, attempt - 1));
        continue;
      }
      throw e;
    }

    if (response.ok) {
      break;
    }

    if (attempt < retries && retryOnStatusCodes.includes(response.status) && !(signal?.aborted)) {
      attempt++;
      await delay(retryDelayMs * Math.pow(2, attempt - 1));
      continue;
    }

    throw new Error(await readProblemMessage(response, path));
  }

  if (!response || !response.ok) {
    throw new Error("Impossibile stabilire la connessione di streaming.");
  }

  const reader = response.body?.getReader();
  if (!reader) return;

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() ?? "";

    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed.startsWith("data: ")) continue;
      const dataStr = trimmed.slice(6);
      if (dataStr === "[DONE]") break;
      try {
        const parsed = JSON.parse(dataStr) as { chunk?: string; error?: string };
        if (parsed.error) {
          throw new Error(parsed.error);
        }
        if (parsed.chunk) {
          onChunk(parsed.chunk);
        }
      } catch (e) {
        if (e instanceof Error && !e.message.includes("JSON")) throw e;
      }
    }
  }
}

export async function apiAgentStreamRequest(
  path: string,
  body: unknown,
  onEvent: (event: unknown) => void,
  signal?: AbortSignal,
  options?: ApiRequestOptions
): Promise<void> {
  const {
    retries = DEFAULT_RETRIES,
    retryDelayMs = DEFAULT_RETRY_DELAY_MS,
    retryOnStatusCodes = DEFAULT_RETRY_STATUS_CODES
  } = options ?? {};

  const baseUrl = resolveBackendBaseUrl();
  if (!baseUrl) {
    throw new Error(resolveBackendErrorMessage() ?? "Il backend locale non è disponibile. Riavviare l'applicazione.");
  }

  const requestUrl = resolveBackendRequestUrl(path, baseUrl);
  const headers = new Headers();
  headers.set("Content-Type", "application/json");

  const sessionToken = resolveBackendSessionToken();
  if (!sessionToken) {
    throw new Error("Il token di sessione del backend locale non è disponibile. Riavviare l'applicazione.");
  }
  headers.set(sessionToken.headerName, sessionToken.token);

  let response: Response | undefined;
  let attempt = 0;

  while (attempt <= retries) {
    try {
      response = await fetch(requestUrl, {
        method: "POST",
        headers,
        body: JSON.stringify(body),
        signal
      });
    } catch (e) {
      if (attempt < retries && !(signal?.aborted)) {
        attempt++;
        await delay(retryDelayMs * Math.pow(2, attempt - 1));
        continue;
      }
      throw e;
    }

    if (response.ok) {
      break;
    }

    if (attempt < retries && retryOnStatusCodes.includes(response.status) && !(signal?.aborted)) {
      attempt++;
      await delay(retryDelayMs * Math.pow(2, attempt - 1));
      continue;
    }

    throw new Error(await readProblemMessage(response, path));
  }

  if (!response || !response.ok) {
    throw new Error("Impossibile stabilire la connessione di streaming agent.");
  }

  const reader = response.body?.getReader();
  if (!reader) return;

  const decoder = new TextDecoder();
  let buffer = "";

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split("\n");
    buffer = lines.pop() ?? "";

    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed.startsWith("data: ")) continue;
      const dataStr = trimmed.slice(6);
      if (dataStr === "[DONE]") return;
      try {
        const parsed = JSON.parse(dataStr) as unknown;
        onEvent(parsed);
      } catch (err) {
        console.warn("[apiAgentStreamRequest] Errore di parsing dell'evento SSE:", err, "Dati grezzi:", dataStr);
      }
    }
  }
}

function resolveBackendRequestUrl(path: string, baseUrl: string): URL {
  const url = new URL(path, baseUrl);
  const backendOrigin = new URL(baseUrl).origin;
  if (url.origin !== backendOrigin || !url.pathname.startsWith("/api/")) {
    throw new Error("Percorso API locale non valido.");
  }

  return url;
}

async function readProblemMessage(response: Response, path?: string): Promise<string> {
  try {
    const payload = (await response.json()) as ApiProblemDetails;
    const msg = payload.detail ?? payload.title;
    if (msg && msg.trim().length > 0) {
      return msg;
    }
  } catch {
    // Risposta non JSON
  }

  if (response.status === 404) {
    return path
      ? `Risorsa o endpoint non trovato (404 per ${path}). Verificare che il backend locale e il servizio Ollama siano attivi.`
      : "Risorsa o endpoint non trovato (404). Verificare che il backend locale e il servizio Ollama siano attivi.";
  }

  return `Richiesta fallita con stato ${response.status}.`;
}
