import { act, fireEvent, render, screen } from "@testing-library/react";
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

describe("App initial setup OCR GPU residual checks", () => {
  it("auto-enables OCR GPU after startup wizard provisioning completes", async () => {
    const promptAnalysis = createOcrStartupAnalysis({
      shouldPrompt: true,
      isOcrConfigured: false,
      isNvidiaRuntimeAvailable: true,
      recommendedRuntimeTarget: "nvidia",
      title: "OCR GPU da installare",
      message: "Prepara il runtime OCR locale."
    });
    const configuredAnalysis = createOcrStartupAnalysis({
      ...promptAnalysis,
      shouldPrompt: false,
      isOcrConfigured: true,
      isGpuUsable: true,
      title: "",
      message: ""
    });
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
          body: provisionStarted ? createUsableGpuDiagnostics() : createDiagnostics()
        })
      },
      {
        path: "/api/dependencies/ocr/startup-analysis",
        handler: () => ({ body: provisionStarted && ocrStatusRequests >= 2 ? configuredAnalysis : promptAnalysis })
      },
      {
        path: "/api/dependencies/ocr",
        handler: () => {
          ocrStatusRequests += 1;
          return {
            body: createOcrProvisionStatus({
              isConfigured: provisionStarted && ocrStatusRequests >= 3,
              isRunning: provisionStarted && ocrStatusRequests < 3,
              message:
                provisionStarted && ocrStatusRequests >= 3
                  ? "OCR configurato."
                  : "Configurazione OCR runtime in corso.",
              resolvedRuntime: provisionStarted && ocrStatusRequests >= 3 ? "cuda126" : "cpu",
              startedAtUtc: provisionStarted ? "2026-05-24T14:00:00Z" : null,
              updatedAtUtc: "2026-05-24T14:03:00Z"
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

    const installBtn = await screen.findByRole("button", { name: "Installa OCR GPU" });

    vi.useFakeTimers();
    fireEvent.click(installBtn);
    await flushPromises();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3_000);
    });
    await flushPromises();

    expect(api.calls.some((call) => call.path === "/api/settings/ocr/auto-enable-gpu")).toBe(true);
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
