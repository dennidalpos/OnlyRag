import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import App from "../src/App";
import {
  createAppStatus,
  createOcrProvisionStatus,
  createOcrSettings,
  createOcrStartupAnalysis,
  createRequiredModels,
  flushPromises
} from "./App.testHelpers";
import { mockApi } from "../src/test/apiMock";
import {
  createDiagnostics,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus
} from "../src/test/fixtures";

afterEach(() => {
  vi.useRealTimers();
  localStorage.clear();
});

describe("App OCR startup polling", () => {
  it("auto-enables OCR GPU after the initial diagnostics report usable support", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createUsableGpuDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: createOcrStartupAnalysis({ isNvidiaRuntimeAvailable: true, isGpuUsable: true })
      },
      {
        path: "/api/dependencies/ocr",
        response: createOcrProvisionStatus({
          resolvedRuntime: "cuda129",
          runtimeDetail: "NVIDIA compatibile."
        })
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

    await waitFor(() => {
      expect(api.calls.some((call) => call.path === "/api/settings/ocr/auto-enable-gpu")).toBe(true);
    });
  });

  it("polls OCR provisioning status until the startup wizard can close", async () => {
    vi.useFakeTimers();

    const promptAnalysis = createOcrStartupAnalysis({
      shouldPrompt: true,
      isOcrConfigured: false,
      recommendedRuntimeTarget: "cpu",
      title: "OCR CPU da installare",
      message: "Prepara il runtime OCR locale."
    });
    const configuredAnalysis = createOcrStartupAnalysis({
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      title: "",
      message: ""
    });
    let provisionStarted = false;
    let ocrStatusRequests = 0;

    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => ({ body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;
          return {
            body:
              provisionStarted && ocrStatusRequests >= 3
                ? createOcrProvisionStatus({
                    runtimeTarget: "cpu",
                    startedAtUtc: "2026-05-24T14:00:00Z",
                    updatedAtUtc: "2026-05-24T14:03:00Z"
                  })
                : createOcrProvisionStatus({
                    isConfigured: false,
                    isRunning: provisionStarted,
                    message: provisionStarted ? "Configurazione OCR runtime cpu in corso." : "OCR non configurato.",
                    runtimeTarget: "cpu",
                    runtimeDetail: provisionStarted ? "Installazione pacchetti PaddleOCR in corso." : null,
                    startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
                    updatedAtUtc: provisionStarted ? "2026-05-24T14:01:00Z" : null,
                    progressPercent: 12.5
                  })
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
      }
    ]);

    render(<App />);
    await act(async () => {
      await flushPromises();
    });

    fireEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));
    await act(async () => {
      await flushPromises();
    });

    expect(screen.getByText("Configurazione OCR in corso")).toBeInTheDocument();
    expect(screen.getByText("Installazione pacchetti PaddleOCR in corso.")).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000);
    });

    expect(screen.queryByRole("region", { name: "Notifica di configurazione" })).not.toBeInTheDocument();
  });

  it("shows the final OCR verification status when another startup check remains open", async () => {
    vi.useFakeTimers();

    const promptAnalysis = createOcrStartupAnalysis({
      shouldPrompt: true,
      isOcrConfigured: false,
      recommendedRuntimeTarget: "cpu",
      title: "OCR CPU da installare",
      message: "Prepara il runtime OCR locale."
    });
    const configuredAnalysis = createOcrStartupAnalysis({
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      title: "",
      message: ""
    });
    let provisionStarted = false;
    let ocrStatusRequests = 0;

    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings({ defaultChatModel: null }) },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => ({ body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;
          return {
            body:
              provisionStarted && ocrStatusRequests >= 3
                ? createOcrProvisionStatus({
                    message: "OCR configurato: PaddleOCR 3.3.1.",
                    runtimeTarget: "cpu",
                    runtimeDetail: "Verifica OCR completata.",
                    startedAtUtc: "2026-05-24T14:00:00Z",
                    updatedAtUtc: "2026-05-24T14:04:00Z"
                  })
                : createOcrProvisionStatus({
                    isConfigured: false,
                    isRunning: provisionStarted,
                    message: provisionStarted ? "Installazione pacchetti PaddleOCR in corso." : "OCR non configurato.",
                    runtimeTarget: "cpu",
                    runtimeDetail: provisionStarted ? "Questa fase può durare diversi minuti." : null,
                    startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
                    updatedAtUtc: provisionStarted ? "2026-05-24T14:02:00Z" : null,
                    progressPercent: 12.5
                  })
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
      }
    ]);

    render(<App />);
    await flushPromises();

    fireEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));
    await flushPromises();
    expect(screen.getByText("Installazione pacchetti PaddleOCR in corso.")).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000);
    });

    expect(screen.getByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR configurato: PaddleOCR 3.3.1.")).toBeInTheDocument();
    expect(screen.getByText("Verifica OCR completata.")).toBeInTheDocument();
    expect(screen.getByText(/Ultima verifica:/)).toBeInTheDocument();
  });

  it("prefers the verified OCR status over a stale startup prompt", async () => {
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings({ defaultChatModel: null }) },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: createOcrStartupAnalysis({
          shouldPrompt: true,
          isOcrConfigured: false,
          title: "OCR CPU da installare",
          message: "OnlyRag non vede ancora un runtime PaddleOCR installato."
        })
      },
      {
        path: "/api/dependencies/ocr",
        response: createOcrProvisionStatus({
          message: "OCR configurato: PaddleOCR 3.3.1.",
          runtimeDetail: "Runtime CPU verificato.",
          startedAtUtc: "2026-05-24T14:00:00Z",
          updatedAtUtc: "2026-05-24T14:04:00Z"
        })
      }
    ]);

    render(<App />);

    expect(await screen.findByText("Modello chat non configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR configurato")).toBeInTheDocument();
    expect(screen.getByText("OCR configurato: PaddleOCR 3.3.1.")).toBeInTheDocument();
    expect(screen.getByText("Runtime CPU verificato.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Configura OCR" })).not.toBeInTheDocument();
  });

  it("rechecks OCR prerequisites when the app window becomes active again", async () => {
    let ocrStartupChecks = 0;
    mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => {
          ocrStartupChecks += 1;
          return {
            body: createOcrStartupAnalysis({
              title: "OCR configurato",
              message: "Runtime OCR locale disponibile."
            })
          };
        }
      },
      { path: "/api/dependencies/ocr", response: createOcrProvisionStatus() }
    ]);

    render(<App />);

    await waitFor(() => {
      expect(ocrStartupChecks).toBe(1);
    });
    await flushPromises();

    window.dispatchEvent(new Event("focus"));

    await waitFor(() => {
      expect(ocrStartupChecks).toBe(2);
    });
  });
});

function createUsableGpuDiagnostics() {
  return createDiagnostics({
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
      packageVersions: {},
      capabilityStatus: "usable"
    }
  });
}
