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

    await fulfillJson(route, { detail: `Unhandled route ${method} ${path}` }, 200);
  });

  await page.goto("/");

  // Test Ctrl+1 -> Chat section
  await page.keyboard.press("Control+1");
  await expect(page.getByRole("heading", { name: "Chat", exact: true })).toBeVisible();

  // Test Ctrl+3 -> Documents section
  await page.keyboard.press("Control+3");
  await expect(page.getByRole("heading", { name: "Documenti", exact: true })).toBeVisible();

  // Test Ctrl+6 -> Settings section
  await page.keyboard.press("Control+6");
  await expect(page.getByRole("heading", { name: "Impostazioni", exact: true })).toBeVisible();

  // Test Ctrl+2 -> Coding section
  await page.keyboard.press("Control+2");
  await expect(page.getByRole("heading", { name: "Coding", exact: true })).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
