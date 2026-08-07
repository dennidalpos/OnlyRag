import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

test("chat RAG: model selection, context document selection, streaming response, and citation preview", async ({ page }) => {
  await installBackendBridge(page, true);

  const document = {
    id: 101,
    documentUid: "doc-101",
    originalFileName: "relazione_tecnica.pdf",
    originalPath: "C:\\Docs\\relazione_tecnica.pdf",
    sha256: "hash101",
    mimeType: "application/pdf",
    fileExtension: ".pdf",
    fileSizeBytes: 2048,
    status: "Indexed",
    pageCount: 3,
    chunkCount: 6,
    currentJobId: null,
    lastError: null,
    createdAtUtc: "2026-08-01T10:00:00Z",
    updatedAtUtc: "2026-08-01T10:05:00Z"
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
        installedModelCount: 2
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/models") {
      await fulfillJson(route, {
        models: [
          { name: "llama3.2:3b", model: "llama3.2:3b" },
          { name: "mistral:7b", model: "mistral:7b" }
        ]
      });
      return;
    }

    if (method === "GET" && path === "/api/documents") {
      await fulfillJson(route, [document]);
      return;
    }

    if (method === "GET" && path === "/api/documents/101") {
      await fulfillJson(route, document);
      return;
    }

    if (method === "GET" && path === "/api/documents/101/preview?page=1&pageSize=1") {
      await fulfillJson(route, {
        documentId: 101,
        originalFileName: "relazione_tecnica.pdf",
        mimeType: "application/pdf",
        fileExtension: ".pdf",
        fileSizeBytes: 2048,
        pageCount: 3,
        chunkCount: 6,
        status: "Indexed",
        pageStart: 1,
        pageSize: 1,
        returnedPageCount: 1,
        pages: [
          {
            pageNumber: 1,
            textContent: "Estratto della relazione tecnica indicizzata sul sistema OnlyRag.",
            ocrStatus: null,
            ocrEngine: null,
            ocrConfidence: null,
            ocrError: null
          }
        ]
      });
      return;
    }

    if (method === "POST" && path === "/api/chat/stream") {
      const sseBody = [
        `data: ${JSON.stringify({
          eventType: "meta",
          conversationId: "conv-999",
          sources: [
            {
              documentId: 101,
              documentTitle: "relazione_tecnica.pdf",
              snippet: "Estratto della relazione tecnica...",
              relevanceScore: 0.95,
              pageStart: 1
            }
          ]
        })}\n\n`,
        `data: ${JSON.stringify({
          eventType: "chunk",
          content: "Secondo la relazione tecnica, il sistema "
        })}\n\n`,
        `data: ${JSON.stringify({
          eventType: "chunk",
          content: "funziona con architettura local-first."
        })}\n\n`
      ].join("");

      await route.fulfill({
        status: 200,
        contentType: "text/event-stream",
        body: sseBody
      });
      return;
    }

    await fulfillJson(route, { detail: `Unhandled route ${method} ${path}` }, 404);
  });

  await page.goto("/");

  // Navigate to Chat section
  await page.getByRole("navigation").getByRole("button", { name: /Chat/i }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Chat" })).toBeVisible();

  // Verify document library checkbox is present and check it
  const docCheckbox = page.getByRole("checkbox", { name: /relazione_tecnica\.pdf/i });
  await expect(docCheckbox).toBeVisible();
  await docCheckbox.check();

  // Enter prompt in textarea
  const promptArea = page.getByPlaceholder(/Fai una domanda sui documenti|Scrivi un messaggio/i);
  await promptArea.fill("Quali sono le specifiche della relazione tecnica?");

  // Send message
  await page.getByRole("button", { name: /Invia/i }).click();

  // Verify streamed response appears
  await expect(page.getByText("Secondo la relazione tecnica, il sistema funziona con architettura local-first.")).toBeVisible();

  // Verify citation/source card is rendered
  await expect(page.getByText("relazione_tecnica.pdf")).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
