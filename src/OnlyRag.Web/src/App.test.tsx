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
          title: "Configura OCR",
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
    expect(screen.getAllByText("Configura OCR").length).toBeGreaterThan(0);
    expect(screen.getByText("12.5%")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: "Configura OCR" }));

    await waitFor(() => {
      const provisionCall = api.calls.find((call) => call.path === "/api/dependencies/ocr/provision");
      expect(provisionCall).toBeDefined();
      expect(JSON.parse(String(provisionCall?.body))).toEqual({ confirmed: true, runtimeTarget: "cpu" });
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
      title: "Configura OCR",
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

    fireEvent.click(screen.getByRole("button", { name: "Configura OCR" }));
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
      title: "Configura OCR",
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

    fireEvent.click(screen.getByRole("button", { name: "Configura OCR" }));
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
          recommendedRuntimeTarget: "cpu",
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
