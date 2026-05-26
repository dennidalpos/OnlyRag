import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

type ImportedDocument = {
  id: number;
  documentUid: string;
  originalFileName: string;
  originalPath: string;
  sha256: string | null;
  mimeType: string | null;
  fileExtension: string | null;
  fileSizeBytes: number;
  status: string;
  pageCount: number;
  chunkCount: number;
  currentJobId: string | null;
  lastError: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

test("smoke: import, job state, backend offline, and preview modal", async ({ page }) => {
  const document = createDocument();
  const jobs = [
    {
      id: "job-1",
      type: "document-ingestion",
      status: "Running",
      priority: 0,
      progressPercent: 45,
      currentStep: "Importazione in corso",
      error: null,
      updatedAt: "2026-05-21T12:10:00Z"
    }
  ];
  let documents: ImportedDocument[] = [];

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
        jobQueue: String(jobs.length),
        ollama: "Ready",
        startedAtUtc: "2026-05-21T12:00:00Z",
        lowResourceMode: false
      });
      return;
    }

    if (method === "GET" && path === "/api/settings/ollama") {
      await fulfillJson(route, {
        ollamaBaseUrl: "http://localhost:11434",
        defaultChatModel: "llama3.2:3b",
        defaultEmbeddingModel: "nomic-embed-text",
        defaultTranslationModel: "llama3.2:3b",
        requestTimeoutSeconds: 120,
        embeddingBatchSize: 1,
        embeddingNumCtx: null,
        chatNumCtx: null,
        translationNumCtx: null,
        trustNonLocalEndpoint: false
      });
      return;
    }

    if (method === "GET" && path === "/api/ollama/status") {
      await fulfillJson(route, {
        state: "Ready",
        isReachable: true,
        baseUrl: "http://localhost:11434",
        installedModelCount: 2,
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
          {
            name: "llama3.2:3b",
            model: "llama3.2:3b",
            modifiedAt: "2026-05-21T12:00:00Z",
            size: 2_000_000_000,
            digest: "digest",
            family: "llama",
            parameterSize: "3B",
            quantizationLevel: "Q4"
          },
          {
            name: "nomic-embed-text",
            model: "nomic-embed-text",
            modifiedAt: "2026-05-21T12:00:00Z",
            size: 500_000_000,
            digest: "embedding-digest",
            family: "nomic",
            parameterSize: null,
            quantizationLevel: null
          }
        ]
      });
      return;
    }

    if (method === "GET" && path === "/api/diagnostics/vector-health") {
      await fulfillJson(route, {
        backendName: "Qdrant gRPC",
        storagePersistent: true,
        vectorLimit: 100000,
        totalVectors: 4,
        nearLimit: false,
        warning: null
      });
      return;
    }

    if (method === "GET" && path === "/api/ocr/languages") {
      await fulfillJson(route, [{ code: "it", label: "Italiano", scriptGroup: "Principali", isDefault: true }]);
      return;
    }

    if (method === "GET" && path === "/api/documents") {
      await fulfillJson(route, documents);
      return;
    }

    if (method === "POST" && path === "/api/documents/import") {
      documents = [document];
      await fulfillJson(route, {
        documents: [{ document, deduplicated: false, message: "Importato." }],
        results: [
          {
            fileName: document.originalFileName,
            document,
            deduplicated: false,
            succeeded: true,
            message: "Importato.",
            errorCode: null
          }
        ],
        hasFailures: false
      });
      return;
    }

    if (method === "GET" && path === "/api/documents/1") {
      await fulfillJson(route, document);
      return;
    }

    if (method === "GET" && path === "/api/documents/1/embedding-status") {
      await fulfillJson(route, {
        documentId: 1,
        state: "Completed",
        model: "nomic-embed-text",
        chunkCount: 4,
        embeddedChunkCount: 4,
        progressPercent: 100,
        currentJobId: null,
        currentStep: null,
        vectorSearchBackend: "Qdrant gRPC",
        lastEmbeddedAtUtc: "2026-05-21T12:05:00Z"
      });
      return;
    }

    if (method === "GET" && path === "/api/documents/1/ocr-status") {
      await fulfillJson(route, {
        documentId: 1,
        state: "Completed",
        pageCount: 2,
        ocrPageCount: 2,
        currentPage: 2,
        totalPages: 2,
        averageConfidence: 0.94,
        currentJobId: null,
        currentStep: null,
        engineName: "PaddleOCR",
        lastError: null
      });
      return;
    }

    if (method === "GET" && path === "/api/documents/1/pipeline-status") {
      const completed = { state: "Completed", error: null, completedAtUtc: "2026-05-21T12:05:00Z" };
      await fulfillJson(route, {
        documentId: 1,
        ocrPolicy: "Auto",
        import: completed,
        analysis: completed,
        ocr: completed,
        textExtraction: completed,
        chunking: completed,
        embedding: completed,
        overallState: "Completed",
        activeJobId: null,
        activeJobType: null
      });
      return;
    }

    if (method === "GET" && path === "/api/documents/1/preview?page=1&pageSize=1") {
      await fulfillJson(route, {
        documentId: 1,
        originalFileName: "contratto.md",
        mimeType: "text/markdown",
        fileExtension: ".md",
        fileSizeBytes: 1024,
        pageCount: 2,
        chunkCount: 4,
        status: "Indexed",
        pageStart: 1,
        pageSize: 1,
        returnedPageCount: 1,
        pages: [
          {
            pageNumber: 1,
            textContent: "Testo anteprima indicizzato",
            ocrStatus: null,
            ocrEngine: null,
            ocrConfidence: null,
            ocrError: null
          }
        ]
      });
      return;
    }

    if (method === "GET" && path === "/api/jobs?limit=100") {
      await fulfillJson(route, jobs);
      return;
    }

    await fulfillJson(route, { detail: `Unhandled route ${method} ${path}` }, 404);
  });

  await page.goto("/");

  await page.getByRole("button", { name: "Documenti" }).click();
  await page.locator("input[type='file']").setInputFiles({
    name: "contratto.md",
    mimeType: "text/markdown",
    buffer: Buffer.from("# Contratto")
  });
  await expect(page.getByText("1 file importato. Analisi e indicizzazione in corso.")).toBeVisible();
  await expect(page.getByRole("option", { name: /contratto\.md Markdown Pronto/ })).toBeVisible();

  await page.getByRole("button", { name: "Operazioni 1" }).click();
  await expect(page.getByText("Importazione documento")).toBeVisible();
  await expect(page.getByText("Importazione in corso")).toBeVisible();

  await page.getByRole("button", { name: "Documenti" }).click();
  await page.getByRole("button", { name: "Anteprima" }).click();
  await expect(page.getByRole("dialog", { name: "Anteprima documento" })).toBeVisible();
  await expect(page.getByText("Testo anteprima indicizzato")).toBeVisible();

  await installBackendBridge(page, false);
  await page.reload();
  await expect(page.getByText("Il backend locale non è raggiungibile.").first()).toBeVisible();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}

function createDocument(): ImportedDocument {
  return {
    id: 1,
    documentUid: "doc-1",
    originalFileName: "contratto.md",
    originalPath: "C:\\Docs\\contratto.md",
    sha256: "abc123",
    mimeType: "text/markdown",
    fileExtension: ".md",
    fileSizeBytes: 1024,
    status: "Indexed",
    pageCount: 2,
    chunkCount: 4,
    currentJobId: null,
    lastError: null,
    createdAtUtc: "2026-05-21T12:00:00Z",
    updatedAtUtc: "2026-05-21T12:05:00Z"
  };
}
