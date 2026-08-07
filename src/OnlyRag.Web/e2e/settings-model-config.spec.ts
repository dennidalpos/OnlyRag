import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

test("settings & model config: view status badges, change model selections, save settings", async ({ page }) => {
  await installBackendBridge(page, true);

  let currentSettings = {
    ollamaBaseUrl: "http://localhost:11434",
    defaultChatModel: "llama3.2:3b",
    defaultCodingModel: "qwen2.5-coder:7b",
    defaultEmbeddingModel: "nomic-embed-text",
    defaultTranslationModel: "llama3.2:3b",
    requestTimeoutSeconds: 120,
    embeddingBatchSize: 1,
    embeddingNumCtx: null,
    chatNumCtx: null,
    translationNumCtx: null,
    trustNonLocalEndpoint: false
  };

  await page.route(`${apiBaseUrl}/api/**`, async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    const path = `${url.pathname}${url.search}`;
    const method = request.method();

    if (method === "GET" && path === "/api/app/status") {
      await fulfillJson(route, {
        backend: "Ready",
        database: "Ready",
        jobQueue: "0",
        ollama: "Ready",
        startedAtUtc: "2026-08-01T10:00:00Z",
        lowResourceMode: false
      });
      return;
    }

    if (method === "GET" && path === "/api/settings/ollama") {
      await fulfillJson(route, currentSettings);
      return;
    }

    if (method === "PUT" && path === "/api/settings/ollama") {
      const payload = JSON.parse(request.postData() ?? "{}");
      currentSettings = { ...currentSettings, ...payload };
      await fulfillJson(route, currentSettings);
      return;
    }

    if (method === "GET" && path === "/api/ollama/status") {
      await fulfillJson(route, {
        state: "Ready",
        isReachable: true,
        baseUrl: "http://localhost:11434",
        installedModelCount: 3,
        message: "Ollama raggiungibile.",
        suggestion: null
      });
      return;
    }

    if (method === "GET" && path === "/api/dependencies/ollama") {
      await fulfillJson(route, {
        cliInstalled: true,
        apiReachable: true,
        installCommand: "https://ollama.com/download",
        networkAccessHint: "Download manuale disponibile."
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/models") {
      await fulfillJson(route, {
        models: [
          { name: "llama3.2:3b", model: "llama3.2:3b" },
          { name: "qwen2.5-coder:7b", model: "qwen2.5-coder:7b" },
          { name: "nomic-embed-text", model: "nomic-embed-text" }
        ]
      });
      return;
    }

    if (method === "GET" && path === "/api/settings/performance") {
      await fulfillJson(route, { profile: "balanced", memoryLimitMb: 8192, gpuAcceleration: true });
      return;
    }

    if (method === "GET" && path === "/api/settings/ingestion") {
      await fulfillJson(route, { chunkSize: 512, chunkOverlap: 64, defaultOcrEnabled: true });
      return;
    }

    if (method === "GET" && path === "/api/settings/ocr-processing") {
      await fulfillJson(route, { device: "cpu", recognitionBatchSize: 8 });
      return;
    }

    if (method === "GET" && path === "/api/settings/ocr") {
      await fulfillJson(route, { device: "cpu", recognitionBatchSize: 8, languages: ["it"] });
      return;
    }

    if (method === "GET" && path === "/api/settings/pdf-export") {
      await fulfillJson(route, { format: "pdf" });
      return;
    }

    if (method === "GET" && path === "/api/pdf-export/status") {
      await fulfillJson(route, { isAvailable: true });
      return;
    }

    if (method === "GET" && path === "/api/dependencies/ocr") {
      await fulfillJson(route, { isConfigured: true, isRunning: false, resolvedRuntime: "cpu", message: "OCR configurato." });
      return;
    }

    if (method === "GET" && path === "/api/diagnostics/vector-health") {
      await fulfillJson(route, {
        backendName: "Qdrant gRPC",
        storagePersistent: true,
        vectorLimit: 100000,
        totalVectors: 12,
        nearLimit: false,
        warning: null
      });
      return;
    }

    if (method === "GET" && path === "/api/ocr/languages") {
      await fulfillJson(route, [{ code: "it", label: "Italiano", scriptGroup: "Principali", isDefault: true }]);
      return;
    }

    if (method === "GET" && path === "/api/diagnostics") {
      await fulfillJson(route, {
        appVersion: "1.0.0",
        databasePath: "C:\\OnlyRag\\onlyrag.db",
        logsDirectory: "C:\\OnlyRag\\logs",
        ollamaStatus: "Ready",
        ollamaIsReachable: true,
        ocrStatus: "Ready",
        ocrIsConfigured: true,
        ocrEngineName: "PaddleOCR",
        ocrGpuCapability: { isUsable: false, capabilityStatus: "no_nvidia_gpu" },
        systemTelemetry: {
          cpu: { logicalProcessorCount: 8, usagePercent: 10 },
          memory: { totalBytes: 16000000000, availableBytes: 8000000000 },
          systemDisk: { name: "C:", totalBytes: 256000000000, availableBytes: 128000000000 },
          gpu: null
        }
      });
      return;
    }

    if (method === "GET" && path === "/api/reranker/info") {
      await fulfillJson(route, { isDownloaded: false, modelId: "bge-reranker-large", sizeBytes: 0 });
      return;
    }

    if (method === "GET" && path === "/api/system/hardware") {
      await fulfillJson(route, { cpuCount: 8, totalRamMb: 16000, freeRamMb: 8000, gpus: [] });
      return;
    }

    if (method === "GET" && path === "/api/settings/logging") {
      await fulfillJson(route, { minLogLevel: "Information", retentionDays: 7 });
      return;
    }

    if (method === "GET" && path === "/api/logs/storage") {
      await fulfillJson(route, { totalFiles: 1, totalSizeBytes: 1024 });
      return;
    }

    if (method === "GET" && path === "/api/workspace/config") {
      await fulfillJson(route, { rootPath: "D:\\Project\\OnlyRag", isAuthorized: true, fileCount: 10 });
      return;
    }

    if (method === "GET" && path === "/api/documents") {
      await fulfillJson(route, []);
      return;
    }

    if (path.endsWith("/models") || path.endsWith("/documents") || path.endsWith("/languages") || path.endsWith("/catalog") || path.startsWith("/api/jobs") || path.startsWith("/api/agent") || path === "/api/images") {
      await fulfillJson(route, []);
      return;
    }

    await fulfillJson(route, {});
  });

  await page.goto("/");
  const settingsBtn = page.getByRole("button", { name: /Impostazioni/i }).first();
  await expect(settingsBtn).toBeVisible();
  await settingsBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Impostazioni" })).toBeVisible();

  // Modify an input to enable the save button
  const urlInput = page.locator("#ollama-url");
  await expect(urlInput).toBeEnabled({ timeout: 10000 });
  await urlInput.fill("http://localhost:11435");

  // Click Save Settings button
  const saveButton = page.getByRole("button", { name: /Salva Impostazioni/i });
  await expect(saveButton).toBeEnabled();
  await saveButton.click();

  // Verify feedback banner
  await expect(page.getByText(/Impostazioni salvate con successo/i)).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
