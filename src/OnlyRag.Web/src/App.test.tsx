import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import App from "./App";
import { mockApi } from "./test/apiMock";
import {
  createDiagnostics,
  createModel,
  createOllamaInstallStatus,
  createOllamaSettings,
  createOllamaStatus
} from "./test/fixtures";

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
          runtimeDetail: null
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
          runtimeDetail: null
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
