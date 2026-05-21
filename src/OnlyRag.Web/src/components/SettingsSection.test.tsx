import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { SettingsSection } from "./SettingsSection";
import { mockApi } from "../test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOcrLanguage,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus,
  createPerformanceSettings
} from "../test/fixtures";

describe("SettingsSection", () => {
  it("saves trusted non-local Ollama settings and reports success", async () => {
    const onDataChanged = vi.fn(async () => {});
    const api = mockApi([
      { path: "/api/settings/office-conversion", response: { libreOfficePath: null, conversionTimeoutSeconds: 120 } },
      {
        path: "/api/office-converter/status",
        response: {
          state: "Missing",
          isAvailable: false,
          executablePath: null,
          message: "LibreOffice non installato.",
          suggestion: null,
          conversionTimeoutSeconds: 120
        }
      },
      { path: "/api/settings/performance", response: createPerformanceSettings() },
      { path: "/api/settings/ingestion", response: { chunkSizeTokens: 800, overlapTokens: 120 } },
      {
        path: "/api/settings/ocr-processing",
        response: { language: "it", maxRetries: 2, pageTimeoutSeconds: 180, lowConfidenceThreshold: 0.55 }
      },
      {
        path: "/api/settings/ocr",
        response: {
          profile: "balanced",
          pdfDpi: 200,
          modelPreset: "PP-OCRv5",
          modelVersion: "PP-OCRv5",
          detectionSideLimit: 960,
          detectionThreshold: 0.3,
          detectionBoxThreshold: 0.6,
          detectionUnclipRatio: 1.5,
          recognitionScoreThreshold: 0.5,
          useTextlineOrientation: true,
          useDocumentOrientationClassification: false,
          useDocumentUnwarping: false,
          recognitionBatchSize: 6,
          cpuThreads: 2,
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: { isConfigured: true, isRunning: false, message: "OCR configurato.", lastError: null }
      },
      {
        path: "/api/settings/ollama",
        method: "PUT",
        handler: (request) => ({
          body: createOllamaSettings(
            JSON.parse(String(request.body)) as Partial<ReturnType<typeof createOllamaSettings>>
          )
        })
      }
    ]);

    render(
      <SettingsSection
        settings={createOllamaSettings()}
        status={createOllamaStatus()}
        models={[createModel()]}
        loadError={null}
        onDataChanged={onDataChanged}
      />
    );

    await userEvent.clear(screen.getByLabelText("URL Ollama"));
    await userEvent.type(screen.getByLabelText("URL Ollama"), "http://192.168.1.50:11434");
    expect(screen.getByText(/Abilita la fiducia solo per un servizio Ollama/)).toBeInTheDocument();

    await userEvent.click(screen.getByLabelText("Considera attendibile questo endpoint Ollama non locale"));
    await userEvent.click(screen.getByRole("button", { name: "Salva impostazioni" }));

    expect(await screen.findByText("Impostazioni Ollama salvate.")).toBeInTheDocument();
    expect(onDataChanged).toHaveBeenCalledOnce();

    const saveCall = api.calls.find((call) => call.path === "/api/settings/ollama" && call.method === "PUT");
    expect(JSON.parse(String(saveCall?.body))).toMatchObject({
      ollamaBaseUrl: "http://192.168.1.50:11434",
      trustNonLocalEndpoint: true
    });
  });

  it("keeps settings usable when diagnostics are unavailable", async () => {
    mockApi([
      { path: "/api/settings/office-conversion", status: 500, response: { detail: "Office non disponibile." } },
      { path: "/api/office-converter/status", status: 500, response: { detail: "Office non disponibile." } },
      { path: "/api/settings/performance", response: createPerformanceSettings() },
      { path: "/api/settings/ingestion", response: { chunkSizeTokens: 800, overlapTokens: 120 } },
      {
        path: "/api/settings/ocr-processing",
        response: { language: "it", maxRetries: 2, pageTimeoutSeconds: 180, lowConfidenceThreshold: 0.55 }
      },
      {
        path: "/api/settings/ocr",
        response: {
          profile: "balanced",
          pdfDpi: 200,
          modelPreset: "PP-OCRv5",
          modelVersion: "PP-OCRv5",
          detectionSideLimit: 960,
          detectionThreshold: 0.3,
          detectionBoxThreshold: 0.6,
          detectionUnclipRatio: 1.5,
          recognitionScoreThreshold: 0.5,
          useTextlineOrientation: true,
          useDocumentOrientationClassification: false,
          useDocumentUnwarping: false,
          recognitionBatchSize: 6,
          cpuThreads: 2,
          device: "cpu"
        }
      },
      { path: "/api/ocr/languages", response: [createOcrLanguage()] },
      { path: "/api/diagnostics", status: 500, response: { detail: "Diagnostica offline." } },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      {
        path: "/api/dependencies/ocr",
        response: { isConfigured: false, isRunning: false, message: "OCR assente.", lastError: null }
      }
    ]);

    render(
      <SettingsSection
        settings={createOllamaSettings()}
        status={createOllamaStatus()}
        models={[createModel()]}
        loadError={null}
        onDataChanged={async () => {}}
      />
    );

    expect(await screen.findByText("Dati diagnostici non disponibili.")).toBeInTheDocument();
    expect(await screen.findByText("Office non disponibile.")).toBeInTheDocument();
  });
});
