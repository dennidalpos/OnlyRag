import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;
const viewports = [320, 520, 700, 980] as const;
const sections = [
  ["Control+1", "Chat"],
  ["Control+2", "Coding"],
  ["Control+3", "Documenti"],
  ["Control+4", "Traduzione"],
  ["Control+5", "Immagini"],
  ["Control+6", "Impostazioni"]
] as const;

test("responsive: every primary section fits widths from 320 to 980 px", async ({ page }, testInfo) => {
  await installBackendBridge(page, true);
  await page.route(`${apiBaseUrl}/api/**`, async (route) => fulfillApiResponse(route));

  for (const width of viewports) {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/");

    for (const [shortcut, heading] of sections) {
      await page.keyboard.press(shortcut);
      await expect(page.getByRole("heading", { level: 1, name: heading })).toBeVisible();
      const documentWidth = await page.locator("html").evaluate((element) => element.scrollWidth);
      expect(documentWidth).toBeLessThanOrEqual(width);
      await page.screenshot({ path: testInfo.outputPath(`responsive-${heading}-${width}.png`), fullPage: true });
    }
  }
});

async function fulfillApiResponse(route: Route) {
  const request = route.request();
  const url = new URL(request.url());
  const path = `${url.pathname}${url.search}`;

  if (request.method() !== "GET") {
    await fulfillJson(route, {});
    return;
  }

  if (path === "/api/app/status") {
    await fulfillJson(route, {
      backend: "Ready",
      database: "Ready",
      jobQueue: "0",
      ollama: "Ready",
      startedAtUtc: "2026-08-03T12:00:00Z",
      lowResourceMode: false
    });
    return;
  }

  if (path === "/api/ollama/status") {
    await fulfillJson(route, { state: "Ready", isReachable: true, installedModelCount: 0 });
    return;
  }

  if (path === "/api/workspace/config") {
    await fulfillJson(route, { rootPath: null, isAuthorized: false, fileCount: 0 });
    return;
  }

  if (path === "/api/diagnostics") {
    await fulfillJson(route, {
      appVersion: "test",
      databasePath: "C:\\OnlyRag\\onlyrag.db",
      logsDirectory: "C:\\OnlyRag\\logs",
      ollamaStatus: "Ready",
      ollamaIsReachable: true,
      ollamaVersion: null,
      ollamaRunningModels: [],
      qdrant: {
        status: "Ready",
        isReachable: true,
        grpcEndpoint: "http://127.0.0.1:6334",
        isLoopback: true,
        isTls: false,
        hasApiKey: false,
        version: null,
        binaryPath: null,
        configPath: null,
        storageDirectory: null,
        processId: null,
        warning: null,
        error: null
      },
      ocrStatus: "Ready",
      ocrIsConfigured: true,
      ocrEngineName: "PaddleOCR",
      ocrGpuCapability: {
        isUsable: false,
        status: "CPU",
        blockReason: null,
        runtimeDetail: null,
        engineVersion: null,
        nvidiaName: null,
        driverVersion: null,
        compiledWithCuda: null,
        cudaDeviceCount: null,
        activeDevice: null,
        packageVersions: {},
        capabilityStatus: "no_nvidia_gpu"
      },
      systemTelemetry: {
        cpu: { logicalProcessorCount: 4, usagePercent: 5 },
        memory: { totalBytes: 16_000_000_000, availableBytes: 8_000_000_000 },
        systemDisk: { name: "C:", totalBytes: 256_000_000_000, availableBytes: 128_000_000_000 },
        gpu: null
      },
      imageGeneration: null
    });
    return;
  }

  if (path.endsWith("/models") || path.endsWith("/documents") || path.endsWith("/languages") || path.startsWith("/api/jobs") || path.startsWith("/api/agent")) {
    await fulfillJson(route, []);
    return;
  }

  await fulfillJson(route, { detail: `Unhandled route ${request.method()} ${path}` }, 404);
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
