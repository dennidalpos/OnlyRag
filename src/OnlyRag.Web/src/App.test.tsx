import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import App from "./App";
import { mockApi } from "./test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus
} from "./test/fixtures";

afterEach(() => {
  vi.useRealTimers();
});

describe("App initial setup", () => {
  it("shows OCR setup in the startup wizard and starts confirmed provisioning", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: {
          shouldPrompt: true,
          isWindowsSupported: true,
          hasMinimumDiskSpace: true,
          availableDiskBytes: 240 * 1024 * 1024 * 1024,
          requiredDiskBytes: 3 * 1024 * 1024 * 1024,
          hasCompatiblePython: true,
          isOcrConfigured: false,
          isNvidiaRuntimeAvailable: false,
          isGpuUsable: false,
          recommendedRuntimeTarget: "cpu",
          title: "Installa OCR CPU",
          message: "Prepara il runtime OCR locale.",
          findings: []
        }
      },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: false,
          isRunning: false,
          message: "OCR non configurato.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: "2026-05-24T14:00:00Z"
        }
      },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        response: { started: true, message: "Configurazione OCR avviata." }
      }
    ]);

    render(<App />);

    expect(await screen.findByRole("dialog", { name: "Configurazione iniziale richiesta" })).toBeInTheDocument();
    expect(screen.getAllByText("Installa OCR CPU").length).toBeGreaterThan(0);
    expect(screen.getByText("12.5%")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));

    await waitFor(() => {
      const provisionCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/provision");
      expect(provisionCall).toBeDefined();
      expect(JSON.parse(String(provisionCall?.body))).toEqual({ confirmed: true, runtimeTarget: "cpu" });
    });
  });

  it("shows a repair action for a damaged OCR runtime in the startup wizard", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      { path: "/api/diagnostics", response: createDiagnostics() },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        response: {
          shouldPrompt: true,
          isWindowsSupported: true,
          hasMinimumDiskSpace: true,
          availableDiskBytes: 143.5 * 1024 * 1024 * 1024,
          requiredDiskBytes: 2 * 1024 * 1024 * 1024,
          hasCompatiblePython: true,
          isOcrConfigured: false,
          isNvidiaRuntimeAvailable: false,
          isGpuUsable: false,
          recommendedRuntimeTarget: "auto",
          title: "Runtime OCR da riparare",
          message:
            "Runtime OCR locale incompleto o danneggiato. Apri Impostazioni > Diagnostica e premi Configura OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.",
          findings: []
        }
      },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: false,
          isRunning: false,
          message:
            "Runtime OCR locale incompleto o danneggiato. Apri Impostazioni > Diagnostica e premi Configura OCR per reinstallare PaddleOCR e il runtime PaddlePaddle corretto.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: "2026-05-24T14:00:00Z"
        }
      },
      {
        path: "/api/dependencies/ocr/provision",
        method: "POST",
        response: { started: true, message: "Configurazione OCR avviata." }
      }
    ]);

    render(<App />);

    expect(await screen.findByText("Runtime OCR da riparare")).toBeInTheDocument();
    expect(screen.getByText(/Premi Ripara OCR per reinstallare PaddleOCR/)).toBeInTheDocument();
    expect(screen.queryByText("unknown")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Ripara OCR" })).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Ripara OCR" }));

    await waitFor(() => {
      const provisionCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/provision");
      expect(provisionCall).toBeDefined();
      expect(JSON.parse(String(provisionCall?.body))).toEqual({ confirmed: true, runtimeTarget: "auto" });
    });
  });

  it("auto-enables OCR GPU after the initial diagnostics report usable support", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus() },
      { path: "/api/ollama/models", response: { models: createRequiredModels() } },
      { path: "/api/documents", response: [] },
      {
        path: "/api/diagnostics",
        response: createDiagnostics({
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
      },
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
          isNvidiaRuntimeAvailable: true,
          isGpuUsable: true,
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
          resolvedRuntime: "cuda129",
          runtimeDetail: "NVIDIA compatibile.",
          startedAtUtc: null,
          updatedAtUtc: "2026-05-24T14:00:00Z"
        }
      },
      {
        path: "/api/settings/ocr/auto-enable-gpu",
        method: "POST",
        response: {
          applied: true,
          message: "OCR GPU abilitata automaticamente.",
          settings: {
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
            recognitionBatchSize: 12,
            cpuThreads: 2,
            device: "gpu"
          }
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

    const promptAnalysis = {
      shouldPrompt: true,
      isWindowsSupported: true,
      hasMinimumDiskSpace: true,
      availableDiskBytes: 240 * 1024 * 1024 * 1024,
      requiredDiskBytes: 3 * 1024 * 1024 * 1024,
      hasCompatiblePython: true,
      isOcrConfigured: false,
      isNvidiaRuntimeAvailable: false,
      isGpuUsable: false,
      recommendedRuntimeTarget: "cpu",
      title: "Installa OCR CPU",
      message: "Prepara il runtime OCR locale.",
      findings: []
    };
    const configuredAnalysis = {
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      title: "",
      message: ""
    };
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
        handler: () => ({
          body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis
        })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;

          if (provisionStarted && ocrStatusRequests >= 3) {
            return {
              body: {
                isConfigured: true,
                isRunning: false,
                message: "OCR configurato.",
                lastError: null,
                runtimeTarget: "cpu",
                resolvedRuntime: "cpu",
                runtimeDetail: null,
                startedAtUtc: "2026-05-24T14:00:00Z",
                updatedAtUtc: "2026-05-24T14:03:00Z"
              }
            };
          }

          return {
            body: {
              isConfigured: false,
              isRunning: provisionStarted,
              message: provisionStarted ? "Configurazione OCR runtime cpu in corso." : "OCR non configurato.",
              lastError: null,
              runtimeTarget: "cpu",
              resolvedRuntime: "cpu",
              runtimeDetail: provisionStarted ? "Installazione pacchetti PaddleOCR in corso." : null,
              startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
              updatedAtUtc: provisionStarted ? "2026-05-24T14:01:00Z" : null
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
      }
    ]);

    render(<App />);
    await flushPromises();

    fireEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));
    await flushPromises();
    expect(screen.getByText("Configurazione OCR in corso")).toBeInTheDocument();
    expect(screen.getByText(/Aggiornamento automatico ogni 5 secondi/)).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    expect(screen.queryByRole("dialog", { name: "Configurazione iniziale richiesta" })).not.toBeInTheDocument();
  });

  it("shows the final OCR verification status when another startup check remains open", async () => {
    vi.useFakeTimers();

    const promptAnalysis = {
      shouldPrompt: true,
      isWindowsSupported: true,
      hasMinimumDiskSpace: true,
      availableDiskBytes: 240 * 1024 * 1024 * 1024,
      requiredDiskBytes: 3 * 1024 * 1024 * 1024,
      hasCompatiblePython: true,
      isOcrConfigured: false,
      isNvidiaRuntimeAvailable: false,
      isGpuUsable: false,
      recommendedRuntimeTarget: "cpu",
      title: "Installa OCR CPU",
      message: "Prepara il runtime OCR locale.",
      findings: []
    };
    const configuredAnalysis = {
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      title: "",
      message: ""
    };
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
        handler: () => ({
          body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis
        })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;

          if (provisionStarted && ocrStatusRequests >= 3) {
            return {
              body: {
                isConfigured: true,
                isRunning: false,
                message: "OCR configurato: PaddleOCR 3.3.1.",
                lastError: null,
                runtimeTarget: "cpu",
                resolvedRuntime: "cpu",
                runtimeDetail: "Verifica OCR completata.",
                startedAtUtc: "2026-05-24T14:00:00Z",
                updatedAtUtc: "2026-05-24T14:04:00Z"
              }
            };
          }

          return {
            body: {
              isConfigured: false,
              isRunning: provisionStarted,
              message: provisionStarted ? "Installazione pacchetti PaddleOCR in corso." : "OCR non configurato.",
              lastError: null,
              runtimeTarget: "cpu",
              resolvedRuntime: "cpu",
              runtimeDetail: provisionStarted ? "Questa fase può durare diversi minuti." : null,
              startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
              updatedAtUtc: provisionStarted ? "2026-05-24T14:02:00Z" : null
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
      }
    ]);

    render(<App />);
    await flushPromises();

    fireEvent.click(screen.getByRole("button", { name: "Installa OCR CPU" }));
    await flushPromises();
    expect(screen.getByText("Installazione pacchetti PaddleOCR in corso.")).toBeInTheDocument();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
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
        response: {
          shouldPrompt: true,
          isWindowsSupported: true,
          hasMinimumDiskSpace: true,
          availableDiskBytes: 240 * 1024 * 1024 * 1024,
          requiredDiskBytes: 3 * 1024 * 1024 * 1024,
          hasCompatiblePython: true,
          isOcrConfigured: false,
          isNvidiaRuntimeAvailable: false,
          isGpuUsable: false,
          recommendedRuntimeTarget: "auto",
          title: "OCR non configurato in OnlyRag",
          message: "OnlyRag non vede ancora un runtime PaddleOCR funzionante.",
          findings: []
        }
      },
      {
        path: "/api/dependencies/ocr",
        response: {
          isConfigured: true,
          isRunning: false,
          message: "OCR configurato: PaddleOCR 3.3.1.",
          lastError: null,
          runtimeTarget: "auto",
          resolvedRuntime: "cpu",
          runtimeDetail: "Runtime CPU verificato.",
          startedAtUtc: "2026-05-24T14:00:00Z",
          updatedAtUtc: "2026-05-24T14:04:00Z"
        }
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
            body: {
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
              title: "OCR configurato",
              message: "Runtime OCR locale disponibile.",
              findings: []
            }
          };
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

    await waitFor(() => {
      expect(ocrStartupChecks).toBe(1);
    });
    await flushPromises();

    window.dispatchEvent(new Event("focus"));

    await waitFor(() => {
      expect(ocrStartupChecks).toBe(2);
    });
  });

  it("keeps Ollama download behind an explicit confirmed action", async () => {
    const api = mockApi([
      { path: "/api/app/status", response: createAppStatus() },
      { path: "/api/settings/ollama", response: createOllamaSettings() },
      { path: "/api/ollama/status", response: createOllamaStatus({ installedModelCount: 2 }) },
      { path: "/api/dependencies/ollama", response: createOllamaInstallStatus({ cliInstalled: false }) },
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
          resolvedRuntime: "configured",
          runtimeDetail: null,
          startedAtUtc: null,
          updatedAtUtc: "2026-05-24T14:00:00Z"
        }
      },
      {
        path: "/api/dependencies/ollama/install",
        method: "POST",
        response: { started: true, message: "Download Ollama aperto." }
      }
    ]);

    render(<App />);

    await userEvent.click(await screen.findByRole("button", { name: "Apri download Ollama" }));

    await waitFor(() => {
      const installCall = api.calls.find((call) => call.path === "/api/dependencies/ollama/install");
      expect(installCall).toBeDefined();
      expect(JSON.parse(String(installCall?.body))).toEqual({ confirmed: true });
    });
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

async function flushPromises() {
  await act(async () => {
    await Promise.resolve();
    await Promise.resolve();
    await Promise.resolve();
  });
}
