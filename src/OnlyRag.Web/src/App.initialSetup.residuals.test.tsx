import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import App from "./App";
import type { OcrSettings } from "./api";
import { mockApi } from "./test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus,
  createPerformanceSettings
} from "./test/fixtures";

afterEach(() => {
  vi.useRealTimers();
});

describe("App initial setup residual checks", () => {
  it("checks chat, embedding, and translation defaults independently on startup", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        response: createOllamaSettings({
          defaultChatModel: null,
          defaultEmbeddingModel: "missing-embed:latest",
          defaultTranslationModel: "missing-translate:latest"
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: {
          shouldPrompt: false,
          isWindowsSupported: true,
          hasMinimumDiskSpace: true,
          availableDiskBytes: 240 * 1024 * 1024 * 1024,
          requiredDiskBytes: 3 * 1024 * 1024 * 1024,
          hasCompatiblePython: true,
          isOcrConfigured: true,
          isNvidiaRuntimeAvailable: false,
          isGpuUsable: false,
          recommendedRuntimeTarget: "auto",
          title: "",
          message: "",
          findings: []
        }
      },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: true,
          isRunning: false,
          message: "OCR configurato.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: "2026-05-24T14:00:00Z"
        }
      }
    ]);

    render(<App />);

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("Modello embedding non disponibile: missing-embed:latest")).toBeInTheDocument();
    expect(screen.getByText("Modello traduzione non disponibile: missing-translate:latest")).toBeInTheDocument();
  });

  it("auto-enables OCR GPU after startup wizard provisioning completes", async () => {
    vi.useFakeTimers();

    const promptAnalysis = {
      shouldPrompt: true,
      isWindowsSupported: true,
      hasMinimumDiskSpace: true,
      availableDiskBytes: 240 * 1024 * 1024 * 1024,
      requiredDiskBytes: 3 * 1024 * 1024 * 1024,
      hasCompatiblePython: true,
      isOcrConfigured: false,
      isNvidiaRuntimeAvailable: true,
      isGpuUsable: false,
      recommendedRuntimeTarget: "nvidia",
      title: "OCR GPU da installare",
      message: "Prepara il runtime OCR locale.",
      findings: []
    };
    const configuredAnalysis = {
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      isGpuUsable: true,
      title: "",
      message: ""
    };
    let provisionStarted = false;
    let ocrStatusRequests = 0;

    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      {
        path: "/api/diagnostics",
        handler: () => ({
          body: provisionStarted
            ? createDiagnostics({
                ocrGpuCapability: {
                  isUsable: true,
                  status: "GPU OCR utilizzabile",
                  blockReason: null,
                  runtimeDetail: "NVIDIA compatibile.",
                  engineVersion: "3.3.1",
                  nvidiaName: "NVIDIA RTX",
                  driverVersion: "596.49",
                  compiledWithCuda: true,
                  cudaDeviceCount: 1,
                  activeDevice: "gpu:0",
                  packageVersions: {}
                }
              })
            : createDiagnostics()
        })
      },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => ({
          body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis
        })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;

          return {
            body: {
              isConfigured: provisionStarted && ocrStatusRequests >= 3,
              isRunning: provisionStarted && ocrStatusRequests < 3,
              message:
                provisionStarted && ocrStatusRequests >= 3
                  ? "OCR configurato."
                  : "Configurazione OCR runtime in corso.",
              lastError: null,
              runtimeTarget: "auto",
              resolvedRuntime: provisionStarted && ocrStatusRequests >= 3 ? "cuda129" : "cpu",
              runtimeDetail: null,
              startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
              updatedAtUtc: "2026-05-24T14:03:00Z"
            }
          };
        }
      },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        handler: () => {
          provisionStarted = true;
          return { body: { started: true, message: "Configurazione OCR avviata." } };
        }
      },
      {
        path: "/api/settings/ocr/auto-enable-gpu",
        method: "POST",
        response: {
          applied: true,
          message: "OCR GPU abilitata automaticamente.",
          settings: createOcrSettings({ device: "gpu", recognitionBatchSize: 12 })
        }
      }
    ]);

    render(<App />);
    await flushPromises();

    fireEvent.click(screen.getByRole("button", { name: "Installa OCR GPU" }));
    await flushPromises();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000);
    });
    await flushPromises();

    expect(api.calls.some((call) => call.path === "/api/settings/ocr/auto-enable-gpu")).toBe(true);
  });

  it("rechecks models and OCR when leaving settings", async () => {
    let needsSetup = false;
    let startupChecks = 0;

    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      {
        path: "/api/settings/ollama",
        handler: () => ({
          body: createOllamaSettings({
            defaultChatModel: needsSetup ? null : "llama3.2:3b"
          })
        })
      },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      {
        path: "/api/diagnostics",
        handler: () => ({
          body: needsSetup
            ? createDiagnostics({ ocrIsConfigured: false, ocrStatus: "Non configurato" })
            : createDiagnostics()
        })
      },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => {
          startupChecks += 1;
          return {
            body: {
              shouldPrompt: needsSetup,
              isWindowsSupported: true,
              hasMinimumDiskSpace: true,
              availableDiskBytes: 240 * 1024 * 1024 * 1024,
              requiredDiskBytes: 2 * 1024 * 1024 * 1024,
              hasCompatiblePython: true,
              isOcrConfigured: !needsSetup,
              isNvidiaRuntimeAvailable: false,
              isGpuUsable: false,
              recommendedRuntimeTarget: "cpu",
              title: needsSetup ? "OCR CPU da installare" : "",
              message: needsSetup ? "Prepara il runtime OCR locale." : "",
              findings: []
            }
          };
        }
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => ({
          body: needsSetup
            ? {
                isConfigured: false,
                isRunning: false,
                message: "OCR non configurato.",
                lastError: null,
                runtimeTarget: "auto",
                resolvedRuntime: "cpu",
                runtimeDetail: null,
                startedAtUtc: null,
                updatedAtUtc: "2026-05-24T14:10:00Z"
              }
            : {
                isConfigured: true,
                isRunning: false,
                message: "OCR configurato.",
                lastError: null,
                runtimeTarget: "auto",
                resolvedRuntime: "cpu",
                runtimeDetail: null,
                startedAtUtc: null,
                updatedAtUtc: "2026-05-24T14:00:00Z"
              }
        })
      },
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
      { path: "/api/settings/ocr", response: createOcrSettings() },
      { path: "/api/ocr/languages", response: [] },
      {
        path: /\/api\/ollama\/models\/details\?name=.*/,
        response: { name: "llama3.2:3b", numCtx: 8192 }
      }
    ]);

    render(<App />);

    await waitFor(() => {
      expect(startupChecks).toBe(1);
    });
    expect(screen.queryByRole("dialog", { name: "Configurazione iniziale richiesta" })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Impostazioni" }));
    await screen.findByRole("heading", { name: "Impostazioni", level: 2 });
    needsSetup = true;
    fireEvent.click(screen.getByRole("button", { name: "Chat" }));

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR CPU da installare")).toBeInTheDocument();
    expect(startupChecks).toBeGreaterThanOrEqual(2);
  });
});

function createRequiredModels() {
  return [
    createModel(),
    createModel({ name: "nomic-embed-text", model: "nomic-embed-text", family: "bert" })
  ];
}

function createAppStatus() {
  return {
    backend: "Ready",
    database: "Ready",
    jobQueue: "0",
    ollama: "Ready",
    startedAtUtc: "2026-05-23T20:00:00Z",
    lowResourceMode: false
  };
}

function createOcrSettings(overrides: Partial<OcrSettings> = {}): OcrSettings {
  return {
    profile: "balanced",
    pdfDpi: 220,
    modelPreset: "PP-OCRv5",
    modelVersion: "PP-OCRv5",
    detectionSideLimit: 1152,
    detectionThreshold: 0.3,
    detectionBoxThreshold: 0.6,
    detectionUnclipRatio: 1.5,
    recognitionScoreThreshold: 0.5,
    useTextlineOrientation: true,
    useDocumentOrientationClassification: false,
    useDocumentUnwarping: false,
    recognitionBatchSize: 6,
    cpuThreads: 2,
    device: "cpu",
    ...overrides
  };
}

async function flushPromises() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}
