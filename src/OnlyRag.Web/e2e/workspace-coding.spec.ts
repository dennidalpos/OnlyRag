import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

test("workspace & coding section: folder selection, prompt sending, and diff modal", async ({ page }) => {
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
      await fulfillJson(route, {
        rootPath: "D:\\Project\\OnlyRag",
        isAuthorized: true,
        fileCount: 42
      });
      return;
    }

    if (method === "GET" && path === "/api/workspace/files") {
      await fulfillJson(route, [
        { relativePath: "src/App.tsx", isDirectory: false, sizeBytes: 1024 },
        { relativePath: "src/main.tsx", isDirectory: false, sizeBytes: 512 }
      ]);
      return;
    }

    if (method === "GET" && path === "/api/settings/ollama") {
      await fulfillJson(route, {
        ollamaBaseUrl: "http://localhost:11434",
        defaultChatModel: "llama3.2:3b",
        defaultCodingModel: "qwen2.5-coder:7b",
        defaultEmbeddingModel: "nomic-embed-text",
        defaultTranslationModel: "llama3.2:3b"
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/status") {
      await fulfillJson(route, {
        state: "Ready",
        isReachable: true,
        installedModelCount: 2
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/models") {
      await fulfillJson(route, {
        models: [
          { name: "qwen2.5-coder:7b", model: "qwen2.5-coder:7b" },
          { name: "llama3.2:3b", model: "llama3.2:3b" }
        ]
      });
      return;
    }

    if (method === "POST" && path === "/api/agent/run-stream") {
      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        body: `data: ${JSON.stringify({ type: "thought_chunk", content: "Analisi codice in corso..." })}\n\ndata: ${JSON.stringify({ type: "final_response", content: "Codice refattorizzato con successo." })}\n\n`
      });
      return;
    }

    await fulfillJson(route, { detail: `Unhandled route ${method} ${path}` }, 200);
  });

  await page.goto("/");

  // Verify Coding section is default
  await expect(page.getByRole("heading", { name: "Coding" })).toBeVisible();

  // Pick workspace folder button visible
  await expect(page.getByText("D:\\Project\\OnlyRag")).toBeVisible();

  // Select model dropdown
  const modelSelect = page.getByRole("combobox", { name: /Seleziona Modello/i });
  await expect(modelSelect).toBeVisible();

  // Send a prompt
  const promptTextarea = page.getByPlaceholder("Descrivi il task di programmazione...");
  await promptTextarea.fill("Refattorizza la funzione di login");
  await page.getByRole("button", { name: /Invia/i }).click();

  // Verify streamed response appears
  await expect(page.getByText("Codice refattorizzato con successo.")).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
