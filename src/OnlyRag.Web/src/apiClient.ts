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

export type HardwareMetricsResponse = {
  cpuUsagePercentage: number;
  memoryAvailableMB: number;
  memoryTotalMB: number;
  powerSource: "ACPower" | "Battery" | "Unknown";
  batteryPercentage?: number | null;
  loadLevel: "Low" | "Normal" | "High" | "Critical";
  energySaverActive: boolean;
  activeProfile: "Performance" | "Balanced" | "Eco";
  recommendedMaxWorkers: number;
  recommendedDelayMs: number;
  sampledAt: string;
};

export type SqliteDatabaseStatus = {
  databasePath: string;
  exists: boolean;
  fileSizeBytes: number;
  formattedFileSize: string;
  fts5Enabled: boolean;
  lastMaintenanceAtUtc?: string | null;
  maintenanceStatus: string;
};

export type SqliteMaintenanceResult = {
  success: boolean;
  initialFileSizeBytes: number;
  finalFileSizeBytes: number;
  bytesReclaimed: number;
  duration: string;
  message: string;
  executedAtUtc: string;
};

export type ExportPreviewRequest = {
  title: string;
  format: "Pdf" | "Docx";
  messages: Array<{
    role: string;
    text: string;
    citations?: Array<{ documentName: string; pageStart?: number; snippet: string }>;
  }>;
  includeCitations?: boolean;
  notes?: string;
  theme?: string;
};

export type ExportPreviewResponse = {
  htmlPreview: string;
  estimatedPageCount: number;
  totalMessageCount: number;
  totalCitationCount: number;
  estimatedFileSizeBytes: number;
  theme: string;
};

export type MultiAgentSubtask = {
  subtaskId: string;
  role: string;
  goal: string;
  dependsOnSubtaskIds: string[];
  status: "Pending" | "Running" | "Completed" | "Failed";
  output?: string | null;
  error?: string | null;
  startedAtUtc?: string | null;
  completedAtUtc?: string | null;
};

export type InterAgentMessage = {
  messageId: string;
  senderRole: string;
  recipientRole: string;
  messageText: string;
  sentAtUtc: string;
};

export type MultiAgentOrchestrationStatus = {
  orchestrationId: string;
  overallGoal: string;
  isCompleted: boolean;
  hasFailed: boolean;
  subtasks: MultiAgentSubtask[];
  messages: InterAgentMessage[];
  startedAtUtc: string;
  finishedAtUtc?: string | null;
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
  let lastError: Error | null = null;

  while (attempt <= retries) {
    try {
      const response = await fetch(requestUrl, {
        ...init,
        headers
      });

      if (!response.ok) {
        if (retryOnStatusCodes.includes(response.status) && attempt < retries) {
          attempt++;
          await delay(retryDelayMs * Math.pow(2, attempt - 1));
          continue;
        }

        const errorMessage = await readProblemMessage(response, path);
        const error = new Error(errorMessage);
        (error as Error & { status?: number }).status = response.status;
        throw error;
      }

      markBackendOnline();
      return (await response.json()) as T;
    } catch (err) {
      lastError = err as Error;
      if (attempt < retries && !(err as Error & { status?: number }).status) {
        attempt++;
        await delay(retryDelayMs * Math.pow(2, attempt - 1));
        continue;
      }
      break;
    }
  }

  if (lastError && !(lastError as Error & { status?: number }).status) {
    markBackendOffline();
  }

  throw lastError ?? new Error("Richiesta API non riuscita.");
}

export async function apiStreamRequest<T = unknown>(
  path: string,
  body: unknown,
  onChunk: (event: T) => void,
  signal?: AbortSignal
): Promise<void> {
  const baseUrl = resolveBackendBaseUrl();
  if (!baseUrl) {
    throw new Error(resolveBackendErrorMessage() ?? "Il backend locale non è disponibile.");
  }
  const requestUrl = resolveBackendRequestUrl(path, baseUrl);
  const sessionToken = resolveBackendSessionToken();
  const headers = new Headers({ "Content-Type": "application/json" });
  if (sessionToken) {
    headers.set(sessionToken.headerName, sessionToken.token);
  }

  const response = await fetch(requestUrl, {
    method: "POST",
    headers,
    body: JSON.stringify(body),
    signal
  });

  if (!response.ok) {
    throw new Error(await readProblemMessage(response, path));
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
        const parsed = JSON.parse(dataStr) as T;
        onChunk(parsed);
      } catch (err) {
        console.warn("[apiStreamRequest] Errore parsing SSE:", err);
      }
    }
  }
}

export async function apiAgentStreamRequest(
  path: string,
  body: unknown,
  onEvent: (event: unknown) => void,
  signal?: AbortSignal
): Promise<void> {
  return apiStreamRequest<unknown>(path, body, onEvent, signal);
}

export async function setHardwareEnergyProfile(profile: "Performance" | "Balanced" | "Eco"): Promise<HardwareMetricsResponse> {
  return apiRequest<HardwareMetricsResponse>("/api/system/hardware/profile", {
    method: "POST",
    body: JSON.stringify({ profile })
  });
}

export async function getDatabaseStatus(): Promise<SqliteDatabaseStatus> {
  return apiRequest<SqliteDatabaseStatus>("/api/system/database/status");
}

export async function runDatabaseMaintenance(): Promise<SqliteMaintenanceResult> {
  return apiRequest<SqliteMaintenanceResult>("/api/system/database/maintenance", {
    method: "POST"
  });
}

export async function getExportPreview(request: ExportPreviewRequest): Promise<ExportPreviewResponse> {
  return apiRequest<ExportPreviewResponse>("/api/export/preview", {
    method: "POST",
    body: JSON.stringify(request)
  });
}

export async function startMultiAgentOrchestration(overallGoal: string): Promise<MultiAgentOrchestrationStatus> {
  return apiRequest<MultiAgentOrchestrationStatus>("/api/agent/orchestrate", {
    method: "POST",
    body: JSON.stringify({ overallGoal })
  });
}

export async function getMultiAgentOrchestrationStatus(id: string): Promise<MultiAgentOrchestrationStatus> {
  return apiRequest<MultiAgentOrchestrationStatus>(`/api/agent/orchestrate/${encodeURIComponent(id)}`);
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
