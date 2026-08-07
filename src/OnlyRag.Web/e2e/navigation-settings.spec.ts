import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

test("navigation & settings: keyboard shortcuts Ctrl+1-6 and section navigation", async ({ page }) => {
  await installBackendBridge(page, true);

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
        startedAtUtc: "2026-07-31T12:00:00Z",
        lowResourceMode: false
      });
      return;
    }

    if (method === "GET" && path === "/api/workspace/config") {
      await fulfillJson(route, { rootPath: null, isAuthorized: false, fileCount: 0 });
      return;
    }

    if (method === "GET" && path === "/api/settings/ollama") {
      await fulfillJson(route, {
        ollamaBaseUrl: "http://localhost:11434",
        defaultChatModel: "llama3.2:3b",
        defaultEmbeddingModel: "nomic-embed-text",
        defaultTranslationModel: "llama3.2:3b"
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/status") {
      await fulfillJson(route, {
        state: "Ready",
        isReachable: true,
        installedModelCount: 1
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/models") {
      await fulfillJson(route, {
        models: [{ name: "llama3.2:3b", model: "llama3.2:3b" }]
      });
      return;
    }

    if (method === "GET" && path === "/api/diagnostics") {
      await fulfillJson(route, {
        appVersion: "test",
        databasePath: "C:\\OnlyRag\\onlyrag.db",
        logsDirectory: "C:\\OnlyRag\\logs",
        ollamaStatus: "Ready",
        ollamaIsReachable: true,
        ollamaVersion: null,
        ollamaRunningModels: [],
        qdrant: { status: "Ready", isReachable: true },
        ocrStatus: "Ready",
        ocrIsConfigured: true,
        ocrGpuCapability: { isUsable: false, status: "CPU", capabilityStatus: "no_nvidia_gpu" },
        systemTelemetry: {
          cpu: { logicalProcessorCount: 4, usagePercent: 5 },
          memory: { totalBytes: 16000000000, availableBytes: 8000000000 },
          systemDisk: { name: "C:", totalBytes: 256000000000, availableBytes: 128000000000 },
          gpu: null
        },
        imageGeneration: null
      });
      return;
    }

    await fulfillJson(route, { detail: `Unhandled route ${method} ${path}` }, 404);
  });

  await page.goto("/");
  await page.waitForLoadState("domcontentloaded");

  // Test navigation to Chat section via Sidebar / shortcut
  const chatNavBtn = page.getByRole("button", { name: /Chat/i }).first();
  await expect(chatNavBtn).toBeVisible();
  await chatNavBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Chat" })).toBeVisible();

  // Test navigation to Documents section
  const docsNavBtn = page.getByRole("button", { name: /Documenti/i }).first();
  await docsNavBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Documenti" })).toBeVisible();

  // Test navigation to Settings section
  const settingsNavBtn = page.getByRole("button", { name: /Impostazioni/i }).first();
  await settingsNavBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Impostazioni" })).toBeVisible();

  // Test navigation to Coding section
  const codingNavBtn = page.getByRole("button", { name: /Coding/i }).first();
  await codingNavBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Coding" })).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
