import { expect, test, type Route } from "@playwright/test";
import { installBackendBridge, mockApiBaseUrl } from "./backendBridge";

const apiBaseUrl = mockApiBaseUrl;

test("image generation & canvas editor: prompt input, generation trigger, toolbar tools, and text overlay", async ({ page }) => {
  await installBackendBridge(page, true);

  const mockGeneratedImage = {
    id: 1,
    fileName: "gen_001.png",
    localPath: "C:\\Images\\gen_001.png",
    prompt: "Un paesaggio montano con nebbia all'alba",
    negativePrompt: "low quality",
    modelId: "sdxl-base-1.0",
    width: 512,
    height: 512,
    steps: 20,
    seed: 42,
    guidanceScale: 7.5,
    createdAtUtc: "2026-08-01T12:00:00Z"
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
        defaultCodingModel: "llama3.2:3b",
        defaultEmbeddingModel: "nomic-embed-text",
        defaultTranslationModel: "llama3.2:3b"
      });
      return;
    }

    if (method === "GET" && path === "/api/dependencies/ollama") {
      await fulfillJson(route, {
        cliInstalled: true,
        apiReachable: true,
        installCommand: null,
        networkAccessHint: null
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

    if (method === "GET" && path === "/api/workspace/config") {
      await fulfillJson(route, { rootPath: null, isAuthorized: false, fileCount: 0 });
      return;
    }

    if (method === "GET" && path === "/api/documents") {
      await fulfillJson(route, []);
      return;
    }

    if (method === "GET" && path === "/api/settings/image-generation") {
      await fulfillJson(route, {
        selectedModelId: "sdxl-base-1.0",
        defaultWidth: 512,
        defaultHeight: 512,
        defaultSteps: 20,
        defaultGuidanceScale: 7.5,
        outputDirectory: "C:\\Images"
      });
      return;
    }

    if (method === "GET" && path === "/api/images/runtime/status") {
      await fulfillJson(route, {
        isDirectMlSupported: true,
        gpuName: "NVIDIA GeForce RTX 4080",
        vramTotalBytes: 17179869184,
        vramFreeBytes: 12884901888,
        activeModelId: "sdxl-base-1.0",
        activeModelState: "Ready"
      });
      return;
    }

    if (method === "GET" && path === "/api/images/models/catalog") {
      await fulfillJson(route, [
        {
          id: "sdxl-base-1.0",
          name: "Stable Diffusion XL 1.0",
          architecture: "SDXL",
          sizeBytes: 6442450944,
          isRecommended: true
        }
      ]);
      return;
    }

    if (method === "GET" && path === "/api/images/models") {
      await fulfillJson(route, [
        { modelId: "sdxl-base-1.0", state: "Ready", isDownloaded: true, isVerified: true }
      ]);
      return;
    }

    if (method === "GET" && path === "/api/images") {
      await fulfillJson(route, [mockGeneratedImage]);
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
        ocrGpuCapability: { isUsable: false, capabilityStatus: "usable" },
        systemTelemetry: {
          cpu: { logicalProcessorCount: 8, usagePercent: 10 },
          memory: { totalBytes: 16000000000, availableBytes: 8000000000 },
          systemDisk: { name: "C:", totalBytes: 256000000000, availableBytes: 128000000000 },
          gpu: { name: "NVIDIA GeForce RTX 4080", usagePercent: 15, memoryUsedBytes: 2000000000, memoryTotalBytes: 16000000000 }
        }
      });
      return;
    }

    if (method === "POST" && path === "/api/images/generate") {
      await fulfillJson(route, mockGeneratedImage);
      return;
    }

    if (path.endsWith("/models") || path.endsWith("/documents") || path.endsWith("/languages") || path.endsWith("/catalog") || path.startsWith("/api/jobs") || path.startsWith("/api/agent") || path === "/api/images") {
      await fulfillJson(route, []);
      return;
    }

    await fulfillJson(route, {});
  });

  await page.goto("/");
  const imagesBtn = page.getByRole("button", { name: /Immagini/i }).first();
  await expect(imagesBtn).toBeVisible();
  await imagesBtn.click();
  await expect(page.getByRole("heading", { level: 1, name: "Immagini" })).toBeVisible();

  // Fill in prompt
  const promptTextarea = page.locator("#image-prompt");
  await expect(promptTextarea).toBeVisible({ timeout: 10000 });
  await promptTextarea.fill("Un paesaggio montano con nebbia all'alba");

  // Trigger generation
  const generateBtn = page.getByRole("button", { name: /Genera Immagine/i });
  await generateBtn.click();

  // Verify generated image is displayed in main view or canvas
  await expect(page.getByRole("img", { name: /Un paesaggio montano/i })).toBeVisible();

  // Select toolbar draw tool
  const drawTool = page.getByTitle(/Disegna a mano libera/i);
  await expect(drawTool).toBeVisible();
  await drawTool.click();

  // Select text tool and add text overlay
  const textTool = page.getByTitle(/Aggiungi testo/i);
  await expect(textTool).toBeVisible();
  await textTool.click();
});

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({
    status,
    contentType: "application/json",
    body: JSON.stringify(body)
  });
}
